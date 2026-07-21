using System.Text.Json;
using System.Text.Json.Serialization;
using MetaQuestFileManager.Core;

return await CliApplication.RunAsync(args);

internal static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0 || HasFlag(arguments, "--help") || HasFlag(arguments, "-h"))
        {
            WriteHelp();
            return 0;
        }

        try
        {
            var client = AdbClient.CreateDefault(GetOption(arguments, "--adb"));
            var executor = new OperatorCommandExecutor(client);
            var command = arguments[0].ToLowerInvariant();
            return command switch
            {
                "devices" => await RunDevicesAsync(executor, arguments),
                "files" => await RunFilesAsync(executor, arguments),
                "apk" => await RunApkAsync(executor, arguments),
                "wifi" => await RunWifiAsync(executor, arguments),
                "kiosk" => await RunKioskAsync(executor, arguments),
                "device" => await RunDeviceAsync(executor, arguments),
                _ => throw new ArgumentException($"Unknown command: {arguments[0]}")
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            FileNotFoundException or
            IOException or
            SplitPackageException)
        {
            Console.Error.WriteLine($"Input error: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunDevicesAsync(
        OperatorCommandExecutor executor,
        string[] arguments)
    {
        var result = await executor.ExecuteAsync(OperatorCommands.DiscoverDevices());
        var devices = result.Devices ?? throw new InvalidOperationException("Device discovery returned no device collection.");
        if (HasFlag(arguments, "--json"))
        {
            WriteJson(devices);
            return 0;
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No ADB devices were found.");
            return 0;
        }

        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Serial}\t{device.State}\t{device.Model ?? "unknown model"}");
        }

        return 0;
    }

    private static async Task<int> RunFilesAsync(
        OperatorCommandExecutor executor,
        string[] arguments)
    {
        var action = RequireAction(arguments, "files");
        var serial = RequireOption(arguments, "--serial");

        switch (action)
        {
            case "list":
                {
                    var remotePath = GetOption(arguments, "--path") ?? "/sdcard";
                    var result = await executor.ExecuteAsync(OperatorCommands.ListFiles(serial, remotePath));
                    var entries = result.RemoteEntries ??
                        throw new InvalidOperationException("File listing returned no entry collection.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(entries);
                    }
                    else
                    {
                        foreach (var entry in entries)
                        {
                            Console.WriteLine($"{entry.TypeLabel}\t{entry.FullPath}");
                        }
                    }

                    return 0;
                }

            case "pull":
                {
                    var remotePath = RequireOption(arguments, "--remote");
                    var outputPath = RequireOption(arguments, "--output");
                    var command = OperatorCommands.PullFile(serial, remotePath, outputPath);
                    await executor.ExecuteAsync(command);
                    Console.WriteLine(command.LocalPath);
                    return 0;
                }

            case "push":
                {
                    var localPath = RequireOption(arguments, "--file");
                    var remotePath = RequireOption(arguments, "--remote");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.PushFile(serial, localPath, remotePath));
                    WriteMutationAware(
                        execution,
                        new { remotePath, command = execution.CommandResult },
                        HasFlag(arguments, "--json"),
                        () => Console.WriteLine(remotePath));
                    return 0;
                }

            default:
                throw new ArgumentException($"Unknown files action: {action}");
        }
    }

    private static async Task<int> RunApkAsync(
        OperatorCommandExecutor executor,
        string[] arguments)
    {
        var action = RequireAction(arguments, "apk");

        switch (action)
        {
            case "list":
                {
                    var serial = RequireOption(arguments, "--serial");
                    var result = await executor.ExecuteAsync(OperatorCommands.ListPackages(serial));
                    var packages = result.Packages ??
                        throw new InvalidOperationException("Package listing returned no package collection.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(packages);
                    }
                    else
                    {
                        foreach (var packageName in packages)
                        {
                            Console.WriteLine(packageName);
                        }
                    }

                    return 0;
                }

            case "export":
                {
                    var serial = RequireOption(arguments, "--serial");
                    var packageName = RequireOption(arguments, "--package");
                    var outputPath = RequireOption(arguments, "--output");
                    var execution = await executor.ExecuteAsync(OperatorCommands.ExportApk(
                        serial,
                        packageName,
                        outputPath,
                        overwrite: HasFlag(arguments, "--overwrite")));
                    var result = execution.ApkExportResult ??
                        throw new InvalidOperationException("APK export returned no export result.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(result);
                    }
                    else
                    {
                        Console.WriteLine($"APK: {result.OutputPath}");
                        Console.WriteLine($"SHA-256: {result.Sha256}");
                        Console.WriteLine($"Checksum: {result.ChecksumPath}");
                    }

                    return 0;
                }

            case "install":
                {
                    var serial = RequireOption(arguments, "--serial");
                    var apkPath = RequireOption(arguments, "--file");
                    var options = ReadInstallOptions(arguments);
                    var execution = await executor.ExecuteAsync(OperatorCommands.InstallApk(serial, apkPath, options));
                    WriteMutationAware(
                        execution,
                        execution.CommandResult,
                        HasFlag(arguments, "--json"),
                        () => Console.WriteLine(execution.CommandResult?.StandardOutput.Trim()));
                    return 0;
                }

            case "install-bundle":
                {
                    var serial = RequireOption(arguments, "--serial");
                    var folderPath = RequireOption(arguments, "--folder");
                    var options = ReadInstallOptions(arguments);
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.InstallApkBundle(serial, folderPath, options));
                    var result = execution.ApkBundleInstallResult ??
                        throw new InvalidOperationException("APK bundle installation returned no result.");
                    WriteMutationAware(
                        execution,
                        result,
                        HasFlag(arguments, "--json"),
                        () =>
                        {
                            Console.WriteLine(result.CommandResult.StandardOutput.Trim());
                            Console.WriteLine($"Installed {result.ApkPaths.Count} APK parts as one package set.");
                        });
                    return 0;
                }

            case "install-many":
                {
                    var serials = GetOptions(arguments, "--serial");
                    var apkPath = RequireOption(arguments, "--file");
                    var options = ReadInstallOptions(arguments);
                    var parallelism = GetIntegerOption(arguments, "--parallelism", 4);
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.InstallApkMany(serials, apkPath, options, parallelism));
                    return WriteParallelInstallResult(
                        execution.ParallelApkInstallResult ??
                        throw new InvalidOperationException("Parallel APK installation returned no result."),
                        execution.MutationReceipt,
                        HasFlag(arguments, "--json"));
                }

            case "install-bundle-many":
                {
                    var serials = GetOptions(arguments, "--serial");
                    var folderPath = RequireOption(arguments, "--folder");
                    var options = ReadInstallOptions(arguments);
                    var parallelism = GetIntegerOption(arguments, "--parallelism", 4);
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.InstallApkBundleMany(serials, folderPath, options, parallelism));
                    return WriteParallelInstallResult(
                        execution.ParallelApkInstallResult ??
                        throw new InvalidOperationException("Parallel APK bundle installation returned no result."),
                        execution.MutationReceipt,
                        HasFlag(arguments, "--json"));
                }

            default:
                throw new ArgumentException($"Unknown apk action: {action}");
        }
    }

    private static async Task<int> RunWifiAsync(
        OperatorCommandExecutor executor,
        string[] arguments)
    {
        var action = RequireAction(arguments, "wifi");
        if (!HasFlag(arguments, "--confirm-wifi-adb"))
        {
            throw new ArgumentException(
                "Wi-Fi ADB changes require --confirm-wifi-adb after operator approval.");
        }

        var port = GetIntegerOption(arguments, "--port", 5555);
        switch (action)
        {
            case "enable":
                {
                    var serial = RequireOption(arguments, "--serial");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.EnableWifiAdb(serial, port, operatorConfirmed: true));
                    var result = execution.WifiAdbEnableResult ??
                        throw new InvalidOperationException("Wi-Fi ADB enablement returned no result.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new { mutation = execution.MutationReceipt, result });
                    }
                    else
                    {
                        Console.WriteLine($"Wi-Fi ADB is connected at {result.Endpoint}.");
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return 0;
                }

            case "connect":
                {
                    var host = RequireOption(arguments, "--host");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.ConnectWifiAdb(host, port, operatorConfirmed: true));
                    var result = execution.WifiAdbConnectionResult ??
                        throw new InvalidOperationException("Wi-Fi ADB connection returned no result.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(result);
                    }
                    else
                    {
                        Console.WriteLine($"Connected to {result.Endpoint}.");
                    }

                    return 0;
                }

            case "disconnect":
                {
                    var host = RequireOption(arguments, "--host");
                    var command = OperatorCommands.DisconnectWifiAdb(
                        host,
                        port,
                        operatorConfirmed: true);
                    var execution = await executor.ExecuteAsync(command);
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new { mutation = execution.MutationReceipt, result = execution.CommandResult });
                    }
                    else
                    {
                        Console.WriteLine($"Disconnected {AndroidInput.CreateWifiEndpoint(host, port)}.");
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return 0;
                }

            default:
                throw new ArgumentException($"Unknown wifi action: {action}");
        }
    }

    private static async Task<int> RunKioskAsync(
        OperatorCommandExecutor executor,
        string[] arguments)
    {
        var action = RequireAction(arguments, "kiosk");
        var serial = RequireOption(arguments, "--serial");
        switch (action)
        {
            case "status":
                {
                    var execution = await executor.ExecuteAsync(OperatorCommands.InspectRustyKiosk(serial));
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new
                        {
                            installation = execution.RustyKioskInstallationStatus,
                            kiosk = execution.RustyKioskOperatorResult
                        });
                    }
                    else
                    {
                        var installation = execution.RustyKioskInstallationStatus ??
                            throw new InvalidOperationException("Rusty Kiosk inspection returned no status.");
                        Console.WriteLine($"Main app: {(installation.MainInstalled ? installation.MainVersion ?? "installed" : "not installed")}");
                        Console.WriteLine($"Setup helper: {(installation.SetupHelperInstalled ? installation.SetupHelperVersion ?? "installed" : "not installed")}");
                        Console.WriteLine($"USB setup: {(installation.SetupHelperReady ? "ready" : "not ready")}");
                        Console.WriteLine($"Host operator: {(installation.HostOperatorAvailable ? "available" : "unavailable")}");
                        if (execution.RustyKioskOperatorResult is { } kiosk)
                        {
                            Console.WriteLine($"Apps: {kiosk.State.InstalledCount} installed, {kiosk.State.NotInstalledCount} not installed");
                            Console.WriteLine($"Accessibility: {(kiosk.State.AccessibilityEnabled ? "enabled" : "disabled")}");
                            Console.WriteLine($"Wi-Fi ADB: {(kiosk.State.WifiAdbEnabled ? "enabled" : "disabled")}");
                        }
                    }

                    return 0;
                }

            case "install":
                {
                    RequireConfirmation(arguments, "--confirm-kiosk-setup", "Rusty Kiosk installation and USB setup");
                    var bundleDirectory = GetOption(arguments, "--bundle");
                    var bundle = RustyKioskBundleLocator.TryFind(bundleDirectory) ??
                        throw new FileNotFoundException(
                            "No bundled Rusty Kiosk APK set was found. Pass --bundle <folder> or stage the release kiosk folder.");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.InstallRustyKiosk(serial, bundle, operatorConfirmed: true));
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new
                        {
                            mutation = execution.MutationReceipt,
                            result = execution.RustyKioskInstallResult
                        });
                    }
                    else
                    {
                        Console.WriteLine("Rusty Kiosk and its setup helper are installed and provisioned.");
                        Console.WriteLine("No Wi-Fi ADB or Accessibility setting was enabled automatically.");
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return 0;
                }

            case "provision":
                {
                    RequireConfirmation(arguments, "--confirm-kiosk-setup", "Rusty Kiosk USB setup");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.ProvisionRustyKiosk(serial, operatorConfirmed: true));
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new
                        {
                            mutation = execution.MutationReceipt,
                            result = execution.RustyKioskProvisionResult
                        });
                    }
                    else
                    {
                        Console.WriteLine("Rusty Kiosk Setup is provisioned.");
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return 0;
                }

            case "command":
                {
                    var command = RustyKioskCommands.Parse(RequireOption(arguments, "--command"));
                    var confirmation = HasFlag(arguments, "--confirm-kiosk-control");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.InvokeRustyKiosk(
                            serial,
                            command,
                            GetOption(arguments, "--value"),
                            operatorConfirmed: confirmation));
                    var result = execution.RustyKioskOperatorResult ??
                        throw new InvalidOperationException("Rusty Kiosk returned no operator result.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new { mutation = execution.MutationReceipt, result });
                    }
                    else
                    {
                        Console.WriteLine(result.Message);
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return result.Accepted ? 0 : 1;
                }

            case "tags":
                {
                    if (arguments.Length < 3 || arguments[2].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("The kiosk tags command requires export or import.");
                    }

                    var tagsAction = arguments[2].ToLowerInvariant();
                    if (tagsAction == "export")
                    {
                        var output = RequireOption(arguments, "--output");
                        await executor.ExecuteAsync(OperatorCommands.PullRustyKioskTags(serial, output));
                        Console.WriteLine(Path.GetFullPath(output));
                        return 0;
                    }

                    if (tagsAction == "import")
                    {
                        RequireConfirmation(arguments, "--confirm-kiosk-control", "Rusty Kiosk tag-file replacement");
                        var input = RequireOption(arguments, "--file");
                        var execution = await executor.ExecuteAsync(
                            OperatorCommands.PushRustyKioskTags(serial, input, operatorConfirmed: true));
                        WriteMutationAware(
                            execution,
                            execution.RustyKioskOperatorResult,
                            HasFlag(arguments, "--json"),
                            () => Console.WriteLine("Rusty Kiosk tag file imported and hotload confirmed."));
                        return 0;
                    }

                    throw new ArgumentException($"Unknown kiosk tags action: {tagsAction}");
                }

            default:
                throw new ArgumentException($"Unknown kiosk action: {action}");
        }
    }

    private static async Task<int> RunDeviceAsync(
        OperatorCommandExecutor executor,
        string[] arguments)
    {
        var action = RequireAction(arguments, "device");
        var serial = RequireOption(arguments, "--serial");
        switch (action)
        {
            case "status":
                {
                    var execution = await executor.ExecuteAsync(OperatorCommands.ReadQuestControls(serial));
                    var result = execution.QuestControlStatus ??
                        throw new InvalidOperationException("Quest status returned no result.");
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(result);
                    }
                    else
                    {
                        Console.WriteLine($"Headset battery: {result.HeadsetBatteryLabel}");
                        Console.WriteLine($"Controllers: {result.ControllerBatteryLabel}");
                        Console.WriteLine($"Keep awake: {(result.KeepAwakeActive ? "active" : "not active")}");
                        Console.WriteLine($"CPU/GPU override: {DisplayOverride(result.CpuLevel)} / {DisplayOverride(result.GpuLevel)}");
                    }

                    return 0;
                }

            case "keep-awake":
                {
                    RequireConfirmation(arguments, "--confirm-device-settings", "Quest keep-awake policy change");
                    var on = HasFlag(arguments, "--on");
                    var off = HasFlag(arguments, "--off");
                    if (on == off)
                    {
                        throw new ArgumentException("Choose exactly one of --on or --off.");
                    }

                    var duration = GetIntegerOption(arguments, "--duration-ms", 28_800_000);
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.SetQuestKeepAwake(
                            serial,
                            enabled: on,
                            durationMilliseconds: duration,
                            operatorConfirmed: true));
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new
                        {
                            mutation = execution.MutationReceipt,
                            result = execution.QuestKeepAwakeResult
                        });
                    }
                    else
                    {
                        Console.WriteLine(on ? "Keep-awake requested." : "Normal power/proximity behavior requested.");
                        Console.WriteLine($"Effective proximity: {execution.QuestControlStatus?.ProximityState ?? "unavailable"}");
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return 0;
                }

            case "performance":
                {
                    RequireConfirmation(arguments, "--confirm-device-settings", "Quest CPU/GPU override change");
                    var clear = HasFlag(arguments, "--clear");
                    var cpu = GetOptionalIntegerOption(arguments, "--cpu");
                    var gpu = GetOptionalIntegerOption(arguments, "--gpu");
                    var execution = await executor.ExecuteAsync(
                        OperatorCommands.SetQuestPerformance(
                            serial,
                            cpu,
                            gpu,
                            clear,
                            operatorConfirmed: true));
                    if (HasFlag(arguments, "--json"))
                    {
                        WriteJson(new
                        {
                            mutation = execution.MutationReceipt,
                            result = execution.QuestPerformanceResult
                        });
                    }
                    else
                    {
                        var status = execution.QuestControlStatus ??
                            throw new InvalidOperationException("Quest performance change returned no readback.");
                        Console.WriteLine($"CPU/GPU override: {DisplayOverride(status.CpuLevel)} / {DisplayOverride(status.GpuLevel)}");
                        WriteMutationReceipt(execution.MutationReceipt);
                    }

                    return 0;
                }

            default:
                throw new ArgumentException($"Unknown device action: {action}");
        }
    }

    private static string RequireAction(string[] arguments, string command)
    {
        if (arguments.Length < 2 || arguments[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The {command} command requires an action.");
        }

        return arguments[1].ToLowerInvariant();
    }

    private static ApkInstallOptions ReadInstallOptions(string[] arguments) =>
        new(
            ReplaceExisting: !HasFlag(arguments, "--no-replace"),
            AllowDowngrade: HasFlag(arguments, "--downgrade"),
            GrantRuntimePermissions: HasFlag(arguments, "--grant-runtime-permissions"),
            AllowTestPackages: HasFlag(arguments, "--test-only"));

    private static string RequireOption(string[] arguments, string name) =>
        GetOption(arguments, name) ?? throw new ArgumentException($"Missing required option {name}.");

    private static string? GetOption(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option {name} requires a value.");
            }

            return arguments[index + 1];
        }

        return null;
    }

    private static IReadOnlyList<string> GetOptions(string[] arguments, string name)
    {
        var values = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option {name} requires a value.");
            }

            values.Add(arguments[index + 1]);
        }

        return values;
    }

    private static int GetIntegerOption(string[] arguments, string name, int defaultValue)
    {
        var value = GetOption(arguments, name);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Option {name} requires an integer value.");
        }

        return parsed;
    }

    private static int? GetOptionalIntegerOption(string[] arguments, string name)
    {
        var value = GetOption(arguments, name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option {name} requires an integer value.");
    }

    private static void RequireConfirmation(string[] arguments, string flag, string operation)
    {
        if (!HasFlag(arguments, flag))
        {
            throw new ArgumentException($"{operation} requires {flag} after operator approval.");
        }
    }

    private static string DisplayOverride(string value) => string.IsNullOrWhiteSpace(value) ? "app controlled" : value;

    private static bool HasFlag(string[] arguments, string name) =>
        arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteMutationAware<T>(
        OperatorExecutionResult execution,
        T result,
        bool json,
        Action writeHumanResult)
    {
        if (json)
        {
            WriteJson(new { mutation = execution.MutationReceipt, result });
            return;
        }

        writeHumanResult();
        WriteMutationReceipt(execution.MutationReceipt);
    }

    private static void WriteMutationReceipt(OperatorMutationReceipt? receipt)
    {
        if (receipt is null)
        {
            return;
        }

        Console.WriteLine(
            $"Sync: {receipt.Stage.ToString().ToLowerInvariant()} " +
            $"({receipt.DesiredState}; observed: {receipt.ObservedState})");
        if (receipt.Stage == OperatorMutationStage.Pending)
        {
            Console.WriteLine("Run the corresponding status command after the wearer responds or the headset settles.");
        }
    }

    private static int WriteParallelInstallResult(
        ParallelApkInstallResult result,
        OperatorMutationReceipt? mutationReceipt,
        bool json)
    {
        if (json)
        {
            WriteJson(new { mutation = mutationReceipt, result });
        }
        else
        {
            foreach (var target in result.Targets)
            {
                Console.WriteLine(
                    $"{target.Serial}\t{(target.Succeeded ? "success" : "failed")}\t{target.Summary}");
            }

            Console.WriteLine(
                $"Installed successfully on {result.SucceededCount} of {result.Targets.Count} headsets " +
                $"with at most {result.MaxParallelism} concurrent installs.");
            WriteMutationReceipt(mutationReceipt);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
            Meta Quest File Manager CLI

            Usage:
              meta-quest-file-manager devices [--json] [--adb <path>]
              meta-quest-file-manager files list --serial <serial> [--path /sdcard] [--json]
              meta-quest-file-manager files pull --serial <serial> --remote <path> --output <path>
              meta-quest-file-manager files push --serial <serial> --file <path> --remote <path>
              meta-quest-file-manager apk list --serial <serial> [--json]
              meta-quest-file-manager apk export --serial <serial> --package <package> --output <file.apk> [--overwrite] [--json]
              meta-quest-file-manager apk install --serial <serial> --file <file.apk> [options]
              meta-quest-file-manager apk install-bundle --serial <serial> --folder <apk-folder> [options]
              meta-quest-file-manager apk install-many --serial <host:port> --serial <host:port> --file <file.apk> [options]
              meta-quest-file-manager apk install-bundle-many --serial <host:port> --serial <host:port> --folder <apk-folder> [options]
              meta-quest-file-manager wifi enable --serial <usb-serial> [--port 5555] --confirm-wifi-adb
              meta-quest-file-manager wifi connect --host <quest-ip> [--port 5555] --confirm-wifi-adb
              meta-quest-file-manager wifi disconnect --host <quest-ip> [--port 5555] --confirm-wifi-adb
              meta-quest-file-manager kiosk status --serial <serial> [--json]
              meta-quest-file-manager kiosk install --serial <usb-serial> [--bundle <folder>] --confirm-kiosk-setup
              meta-quest-file-manager kiosk provision --serial <usb-serial> --confirm-kiosk-setup
              meta-quest-file-manager kiosk command --serial <serial> --command <typed-command> [--value <text>] [--confirm-kiosk-control] [--json]
              meta-quest-file-manager kiosk tags export --serial <serial> --output <app-tags.v1.json>
              meta-quest-file-manager kiosk tags import --serial <serial> --file <app-tags.v1.json> --confirm-kiosk-control
              meta-quest-file-manager device status --serial <serial> [--json]
              meta-quest-file-manager device keep-awake --serial <serial> <--on|--off> [--duration-ms <n>] --confirm-device-settings
              meta-quest-file-manager device performance --serial <serial> [--cpu <0-5>] [--gpu <0-5>] [--clear] --confirm-device-settings

            Install options:
              --no-replace                 Do not reinstall over an existing package.
              --downgrade                  Allow a lower version code.
              --grant-runtime-permissions  Ask Android to grant eligible runtime permissions.
              --test-only                  Allow an APK marked testOnly.
              --parallelism <1-16>          Bound concurrent installs (default: 4).

            Bundle install reads every top-level .apk file in the selected folder and
            sends the complete set through one serial-scoped adb install-multiple call.
            Parallel routes require at least two distinct connected Wi-Fi ADB serials,
            run one serial-scoped install per headset, and report partial failures.
            Enabling Wi-Fi ADB requires a USB-connected authorized headset and explicit
            operator confirmation. Connect/disconnect never reset the global ADB server.
            Rusty Kiosk is optional. Its typed host commands require the installed
            DUMP-protected operator provider and preserve Meta's attended permission prompts.
            Keep-awake, proximity, and CPU/GPU changes require explicit confirmation and
            report effective readback; --clear restores app-controlled performance levels.
            Split APK packages are refused by the single-APK export command.
            """);
    }
}
