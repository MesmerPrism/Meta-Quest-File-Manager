using System.Text.Json;
using MetaQuestFileManager.Core;

return await CliApplication.RunAsync(args);

internal static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
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
                    await executor.ExecuteAsync(OperatorCommands.PushFile(serial, localPath, remotePath));
                    Console.WriteLine(remotePath);
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
                    Console.WriteLine(execution.CommandResult?.StandardOutput.Trim());
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
                    Console.WriteLine(result.CommandResult.StandardOutput.Trim());
                    Console.WriteLine($"Installed {result.ApkPaths.Count} APK parts as one package set.");
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
                        WriteJson(result);
                    }
                    else
                    {
                        Console.WriteLine($"Wi-Fi ADB is connected at {result.Endpoint}.");
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
                        WriteJson(execution.CommandResult);
                    }
                    else
                    {
                        Console.WriteLine($"Disconnected {AndroidInput.CreateWifiEndpoint(host, port)}.");
                    }

                    return 0;
                }

            default:
                throw new ArgumentException($"Unknown wifi action: {action}");
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

    private static bool HasFlag(string[] arguments, string name) =>
        arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static int WriteParallelInstallResult(ParallelApkInstallResult result, bool json)
    {
        if (json)
        {
            WriteJson(result);
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
            Split APK packages are refused by the single-APK export command.
            """);
    }
}
