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
        var serial = RequireOption(arguments, "--serial");

        switch (action)
        {
            case "list":
                {
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
                    var apkPath = RequireOption(arguments, "--file");
                    var options = ReadInstallOptions(arguments);
                    var execution = await executor.ExecuteAsync(OperatorCommands.InstallApk(serial, apkPath, options));
                    Console.WriteLine(execution.CommandResult?.StandardOutput.Trim());
                    return 0;
                }

            case "install-bundle":
                {
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

            default:
                throw new ArgumentException($"Unknown apk action: {action}");
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

    private static bool HasFlag(string[] arguments, string name) =>
        arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

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

            Install options:
              --no-replace                 Do not reinstall over an existing package.
              --downgrade                  Allow a lower version code.
              --grant-runtime-permissions  Ask Android to grant eligible runtime permissions.
              --test-only                  Allow an APK marked testOnly.

            Bundle install reads every top-level .apk file in the selected folder and
            sends the complete set through one serial-scoped adb install-multiple call.
            Split APK packages are refused by the single-APK export command.
            """);
    }
}
