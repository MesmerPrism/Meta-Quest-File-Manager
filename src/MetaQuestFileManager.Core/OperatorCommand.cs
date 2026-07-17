using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MetaQuestFileManager.Core;

public enum OperatorCommandKind
{
    DiscoverDevices,
    ListFiles,
    PullFile,
    PushFile,
    ListPackages,
    ExportApk,
    InstallApk,
    InstallApkBundle,
    EnableWifiAdb,
    ConnectWifiAdb,
    DisconnectWifiAdb,
    InstallApkMany,
    InstallApkBundleMany
}

public sealed class OperatorCommand
{
    internal OperatorCommand(
        OperatorCommandKind kind,
        IReadOnlyList<string> cliArguments,
        string? serial = null,
        string? remotePath = null,
        string? localPath = null,
        string? packageName = null,
        ApkInstallOptions? installOptions = null,
        ApkBundleInput? apkBundle = null,
        IReadOnlyList<string>? serials = null,
        string? wifiHost = null,
        int wifiPort = 5555,
        int maxParallelism = 4,
        bool operatorConfirmed = false,
        bool overwrite = false)
    {
        Kind = kind;
        CliArguments = new ReadOnlyCollection<string>(cliArguments.ToArray());
        Serial = serial;
        RemotePath = remotePath;
        LocalPath = localPath;
        PackageName = packageName;
        InstallOptions = installOptions;
        ApkBundle = apkBundle;
        Serials = serials is null
            ? null
            : new ReadOnlyCollection<string>(serials.ToArray());
        WifiHost = wifiHost;
        WifiPort = wifiPort;
        MaxParallelism = maxParallelism;
        OperatorConfirmed = operatorConfirmed;
        Overwrite = overwrite;
    }

    public OperatorCommandKind Kind { get; }

    public IReadOnlyList<string> CliArguments { get; }

    public string? Serial { get; }

    public string? RemotePath { get; }

    public string? LocalPath { get; }

    public string? PackageName { get; }

    public ApkInstallOptions? InstallOptions { get; }

    public ApkBundleInput? ApkBundle { get; }

    public IReadOnlyList<string>? Serials { get; }

    public string? WifiHost { get; }

    public int WifiPort { get; }

    public int MaxParallelism { get; }

    public bool OperatorConfirmed { get; }

    public bool Overwrite { get; }

    public string ToPowerShellCommand(
        string cliExecutable = ".\\meta-quest-file-manager.exe",
        string? adbPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliExecutable);
        var arguments = CliArguments.ToList();
        if (!string.IsNullOrWhiteSpace(adbPath))
        {
            arguments.Add("--adb");
            arguments.Add(adbPath);
        }

        return $"& {PowerShellCliFormatter.Quote(cliExecutable)} " +
               string.Join(" ", arguments.Select(PowerShellCliFormatter.FormatArgument));
    }
}

public static class OperatorCommands
{
    public static OperatorCommand DiscoverDevices() =>
        new(OperatorCommandKind.DiscoverDevices, ["devices"]);

    public static OperatorCommand EnableWifiAdb(
        string usbSerial,
        int port = 5555,
        bool operatorConfirmed = false)
    {
        RequireWifiApproval(operatorConfirmed);
        usbSerial = AndroidInput.RequireUsbSerial(usbSerial);
        port = AndroidInput.RequireTcpPort(port);
        return new OperatorCommand(
            OperatorCommandKind.EnableWifiAdb,
            [
                "wifi", "enable", "--serial", usbSerial,
                "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--confirm-wifi-adb"
            ],
            serial: usbSerial,
            wifiPort: port,
            operatorConfirmed: true);
    }

    public static OperatorCommand ConnectWifiAdb(
        string host,
        int port = 5555,
        bool operatorConfirmed = false)
    {
        RequireWifiApproval(operatorConfirmed);
        host = AndroidInput.RequireWifiHost(host);
        port = AndroidInput.RequireTcpPort(port);
        return new OperatorCommand(
            OperatorCommandKind.ConnectWifiAdb,
            [
                "wifi", "connect", "--host", host,
                "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--confirm-wifi-adb"
            ],
            wifiHost: host,
            wifiPort: port,
            operatorConfirmed: true);
    }

    public static OperatorCommand DisconnectWifiAdb(
        string host,
        int port = 5555,
        bool operatorConfirmed = false)
    {
        RequireWifiApproval(operatorConfirmed);
        host = AndroidInput.RequireWifiHost(host);
        port = AndroidInput.RequireTcpPort(port);
        return new OperatorCommand(
            OperatorCommandKind.DisconnectWifiAdb,
            [
                "wifi", "disconnect", "--host", host,
                "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--confirm-wifi-adb"
            ],
            wifiHost: host,
            wifiPort: port,
            operatorConfirmed: true);
    }

    public static OperatorCommand ListFiles(string serial, string remotePath)
    {
        serial = AndroidInput.RequireSerial(serial);
        remotePath = AndroidInput.RequireRemotePath(remotePath);
        return new OperatorCommand(
            OperatorCommandKind.ListFiles,
            ["files", "list", "--serial", serial, "--path", remotePath],
            serial: serial,
            remotePath: remotePath);
    }

    public static OperatorCommand PullFile(string serial, string remotePath, string outputPath)
    {
        serial = AndroidInput.RequireSerial(serial);
        remotePath = AndroidInput.RequireRemotePath(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        return new OperatorCommand(
            OperatorCommandKind.PullFile,
            ["files", "pull", "--serial", serial, "--remote", remotePath, "--output", fullOutputPath],
            serial: serial,
            remotePath: remotePath,
            localPath: fullOutputPath);
    }

    public static OperatorCommand PushFile(string serial, string localPath, string remotePath)
    {
        serial = AndroidInput.RequireSerial(serial);
        remotePath = AndroidInput.RequireRemotePath(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        var fullLocalPath = Path.GetFullPath(localPath);
        return new OperatorCommand(
            OperatorCommandKind.PushFile,
            ["files", "push", "--serial", serial, "--file", fullLocalPath, "--remote", remotePath],
            serial: serial,
            remotePath: remotePath,
            localPath: fullLocalPath);
    }

    public static OperatorCommand ListPackages(string serial)
    {
        serial = AndroidInput.RequireSerial(serial);
        return new OperatorCommand(
            OperatorCommandKind.ListPackages,
            ["apk", "list", "--serial", serial],
            serial: serial);
    }

    public static OperatorCommand ExportApk(
        string serial,
        string packageName,
        string outputPath,
        bool overwrite = false)
    {
        serial = AndroidInput.RequireSerial(serial);
        packageName = AndroidInput.RequirePackageName(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var arguments = new List<string>
        {
            "apk", "export", "--serial", serial, "--package", packageName, "--output", fullOutputPath
        };
        if (overwrite)
        {
            arguments.Add("--overwrite");
        }

        return new OperatorCommand(
            OperatorCommandKind.ExportApk,
            arguments,
            serial: serial,
            localPath: fullOutputPath,
            packageName: packageName,
            overwrite: overwrite);
    }

    public static OperatorCommand InstallApk(
        string serial,
        string apkPath,
        ApkInstallOptions? options = null)
    {
        serial = AndroidInput.RequireSerial(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        var fullApkPath = Path.GetFullPath(apkPath);
        options ??= new ApkInstallOptions();
        var arguments = new List<string>
        {
            "apk", "install", "--serial", serial, "--file", fullApkPath
        };
        if (!options.ReplaceExisting)
        {
            arguments.Add("--no-replace");
        }

        if (options.AllowDowngrade)
        {
            arguments.Add("--downgrade");
        }

        if (options.GrantRuntimePermissions)
        {
            arguments.Add("--grant-runtime-permissions");
        }

        if (options.AllowTestPackages)
        {
            arguments.Add("--test-only");
        }

        return new OperatorCommand(
            OperatorCommandKind.InstallApk,
            arguments,
            serial: serial,
            localPath: fullApkPath,
            installOptions: options);
    }

    public static OperatorCommand InstallApkBundle(
        string serial,
        string folderPath,
        ApkInstallOptions? options = null)
    {
        serial = AndroidInput.RequireSerial(serial);
        var bundle = ApkBundleInput.FromFolder(folderPath);
        options ??= new ApkInstallOptions();
        var arguments = new List<string>
        {
            "apk", "install-bundle", "--serial", serial, "--folder", bundle.FolderPath
        };
        if (!options.ReplaceExisting)
        {
            arguments.Add("--no-replace");
        }

        if (options.AllowDowngrade)
        {
            arguments.Add("--downgrade");
        }

        if (options.GrantRuntimePermissions)
        {
            arguments.Add("--grant-runtime-permissions");
        }

        if (options.AllowTestPackages)
        {
            arguments.Add("--test-only");
        }

        return new OperatorCommand(
            OperatorCommandKind.InstallApkBundle,
            arguments,
            serial: serial,
            localPath: bundle.FolderPath,
            installOptions: options,
            apkBundle: bundle);
    }

    public static OperatorCommand InstallApkMany(
        IReadOnlyList<string> serials,
        string apkPath,
        ApkInstallOptions? options = null,
        int maxParallelism = 4)
    {
        var targets = ValidateWifiTargets(serials);
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        var fullApkPath = Path.GetFullPath(apkPath);
        options ??= new ApkInstallOptions();
        maxParallelism = AndroidInput.RequireParallelism(maxParallelism);
        var arguments = new List<string> { "apk", "install-many" };
        AddSerialArguments(arguments, targets);
        arguments.AddRange(
        [
            "--file", fullApkPath,
            "--parallelism", maxParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ]);
        AddInstallOptionArguments(arguments, options);
        return new OperatorCommand(
            OperatorCommandKind.InstallApkMany,
            arguments,
            localPath: fullApkPath,
            installOptions: options,
            serials: targets,
            maxParallelism: maxParallelism);
    }

    public static OperatorCommand InstallApkBundleMany(
        IReadOnlyList<string> serials,
        string folderPath,
        ApkInstallOptions? options = null,
        int maxParallelism = 4)
    {
        var targets = ValidateWifiTargets(serials);
        var bundle = ApkBundleInput.FromFolder(folderPath);
        options ??= new ApkInstallOptions();
        maxParallelism = AndroidInput.RequireParallelism(maxParallelism);
        var arguments = new List<string> { "apk", "install-bundle-many" };
        AddSerialArguments(arguments, targets);
        arguments.AddRange(
        [
            "--folder", bundle.FolderPath,
            "--parallelism", maxParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ]);
        AddInstallOptionArguments(arguments, options);
        return new OperatorCommand(
            OperatorCommandKind.InstallApkBundleMany,
            arguments,
            localPath: bundle.FolderPath,
            installOptions: options,
            apkBundle: bundle,
            serials: targets,
            maxParallelism: maxParallelism);
    }

    private static IReadOnlyList<string> ValidateWifiTargets(IReadOnlyList<string> serials)
    {
        ArgumentNullException.ThrowIfNull(serials);
        if (serials.Count < 2)
        {
            throw new ArgumentException(
                "Select at least two connected Wi-Fi ADB headsets.",
                nameof(serials));
        }

        var targets = serials.Select(AndroidInput.RequireWifiSerial).ToArray();
        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
        {
            throw new ArgumentException("Each Wi-Fi headset may be selected only once.", nameof(serials));
        }

        return targets;
    }

    private static void AddSerialArguments(List<string> arguments, IReadOnlyList<string> serials)
    {
        foreach (var serial in serials)
        {
            arguments.Add("--serial");
            arguments.Add(serial);
        }
    }

    private static void AddInstallOptionArguments(List<string> arguments, ApkInstallOptions options)
    {
        if (!options.ReplaceExisting)
        {
            arguments.Add("--no-replace");
        }

        if (options.AllowDowngrade)
        {
            arguments.Add("--downgrade");
        }

        if (options.GrantRuntimePermissions)
        {
            arguments.Add("--grant-runtime-permissions");
        }

        if (options.AllowTestPackages)
        {
            arguments.Add("--test-only");
        }
    }

    private static void RequireWifiApproval(bool operatorConfirmed)
    {
        if (!operatorConfirmed)
        {
            throw new InvalidOperationException(
                "Wi-Fi ADB changes require explicit operator confirmation.");
        }
    }
}

public sealed record OperatorExecutionResult(
    OperatorCommand Command,
    IReadOnlyList<QuestDevice>? Devices = null,
    IReadOnlyList<RemoteEntry>? RemoteEntries = null,
    IReadOnlyList<string>? Packages = null,
    CommandResult? CommandResult = null,
    ApkExportResult? ApkExportResult = null,
    ApkBundleInstallResult? ApkBundleInstallResult = null,
    WifiAdbEnableResult? WifiAdbEnableResult = null,
    WifiAdbConnectionResult? WifiAdbConnectionResult = null,
    ParallelApkInstallResult? ParallelApkInstallResult = null);

public sealed class OperatorCommandExecutor(AdbClient client)
{
    private readonly AdbClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<OperatorExecutionResult> ExecuteAsync(
        OperatorCommand command,
        CancellationToken cancellationToken = default,
        IProgress<OperatorProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        progress?.Report(new OperatorProgress(
            command.Kind.ToString(),
            StartingMessage(command.Kind),
            0,
            0));
        switch (command.Kind)
        {
            case OperatorCommandKind.DiscoverDevices:
                return new OperatorExecutionResult(
                    command,
                    Devices: await _client.GetDevicesAsync(cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.ListFiles:
                return new OperatorExecutionResult(
                    command,
                    RemoteEntries: await _client.ListRemoteDirectoryAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        Require(command.RemotePath, nameof(command.RemotePath)),
                        cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.PullFile:
                return new OperatorExecutionResult(
                    command,
                    CommandResult: await _client.PullFileAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        Require(command.RemotePath, nameof(command.RemotePath)),
                        Require(command.LocalPath, nameof(command.LocalPath)),
                        cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.PushFile:
                return new OperatorExecutionResult(
                    command,
                    CommandResult: await _client.PushFileAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        Require(command.LocalPath, nameof(command.LocalPath)),
                        Require(command.RemotePath, nameof(command.RemotePath)),
                        cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.ListPackages:
                return new OperatorExecutionResult(
                    command,
                    Packages: await _client.GetThirdPartyPackageNamesAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.ExportApk:
                return new OperatorExecutionResult(
                    command,
                    ApkExportResult: await _client.ExportSingleApkAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        Require(command.PackageName, nameof(command.PackageName)),
                        Require(command.LocalPath, nameof(command.LocalPath)),
                        command.Overwrite,
                        cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.InstallApk:
                return new OperatorExecutionResult(
                    command,
                    CommandResult: await _client.InstallApkAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        Require(command.LocalPath, nameof(command.LocalPath)),
                        command.InstallOptions,
                        cancellationToken).ConfigureAwait(false));

            case OperatorCommandKind.InstallApkBundle:
                {
                    var bundle = command.ApkBundle ??
                        throw new InvalidOperationException("The operator command is missing its APK bundle.");
                    var result = await _client.InstallApkBundleAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        bundle.ApkPaths,
                        command.InstallOptions,
                        cancellationToken).ConfigureAwait(false);
                    return new OperatorExecutionResult(
                        command,
                        CommandResult: result.CommandResult,
                        ApkBundleInstallResult: result);
                }

            case OperatorCommandKind.EnableWifiAdb:
                EnsureWifiApproval(command);
                return new OperatorExecutionResult(
                    command,
                    WifiAdbEnableResult: await _client.EnableWifiAdbAndConnectAsync(
                        Require(command.Serial, nameof(command.Serial)),
                        command.WifiPort,
                        cancellationToken,
                        progress).ConfigureAwait(false));

            case OperatorCommandKind.ConnectWifiAdb:
                EnsureWifiApproval(command);
                return new OperatorExecutionResult(
                    command,
                    WifiAdbConnectionResult: await _client.ConnectWifiAdbAsync(
                        Require(command.WifiHost, nameof(command.WifiHost)),
                        command.WifiPort,
                        cancellationToken,
                        progress).ConfigureAwait(false));

            case OperatorCommandKind.DisconnectWifiAdb:
                EnsureWifiApproval(command);
                return new OperatorExecutionResult(
                    command,
                    CommandResult: await _client.DisconnectWifiAdbAsync(
                        Require(command.WifiHost, nameof(command.WifiHost)),
                        command.WifiPort,
                        cancellationToken,
                        progress).ConfigureAwait(false));

            case OperatorCommandKind.InstallApkMany:
                return new OperatorExecutionResult(
                    command,
                    ParallelApkInstallResult: await _client.InstallApkOnManyWifiDevicesAsync(
                        Require(command.Serials, nameof(command.Serials)),
                        Require(command.LocalPath, nameof(command.LocalPath)),
                        command.InstallOptions,
                        command.MaxParallelism,
                        cancellationToken,
                        progress).ConfigureAwait(false));

            case OperatorCommandKind.InstallApkBundleMany:
                {
                    var bundle = command.ApkBundle ??
                        throw new InvalidOperationException("The operator command is missing its APK bundle.");
                    return new OperatorExecutionResult(
                        command,
                        ParallelApkInstallResult: await _client.InstallApkBundleOnManyWifiDevicesAsync(
                            Require(command.Serials, nameof(command.Serials)),
                            bundle.ApkPaths,
                            command.InstallOptions,
                            command.MaxParallelism,
                            cancellationToken,
                            progress).ConfigureAwait(false));
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown operator command.");
        }
    }

    private static string Require(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"The operator command is missing {name}.");

    private static IReadOnlyList<string> Require(IReadOnlyList<string>? value, string name) =>
        value is { Count: > 0 }
            ? value
            : throw new InvalidOperationException($"The operator command is missing {name}.");

    private static void EnsureWifiApproval(OperatorCommand command)
    {
        if (!command.OperatorConfirmed)
        {
            throw new InvalidOperationException(
                "Wi-Fi ADB changes require explicit operator confirmation.");
        }
    }

    private static string StartingMessage(OperatorCommandKind kind) => kind switch
    {
        OperatorCommandKind.DiscoverDevices => "Looking for authorized headsets…",
        OperatorCommandKind.ListFiles => "Listing the device folder…",
        OperatorCommandKind.PullFile => "Copying the selected file from the headset…",
        OperatorCommandKind.PushFile => "Copying the selected file to the headset…",
        OperatorCommandKind.ListPackages => "Loading third-party packages…",
        OperatorCommandKind.ExportApk => "Exporting and hashing the installed APK…",
        OperatorCommandKind.InstallApk => "Installing the APK…",
        OperatorCommandKind.InstallApkBundle => "Installing the complete APK package set…",
        OperatorCommandKind.EnableWifiAdb => "Preparing Wi-Fi ADB…",
        OperatorCommandKind.ConnectWifiAdb => "Connecting to Wi-Fi ADB…",
        OperatorCommandKind.DisconnectWifiAdb => "Disconnecting Wi-Fi ADB…",
        OperatorCommandKind.InstallApkMany => "Preparing the parallel APK install…",
        OperatorCommandKind.InstallApkBundleMany => "Preparing the parallel APK bundle install…",
        _ => "Working…"
    };
}

internal static partial class PowerShellCliFormatter
{
    public static string FormatArgument(string value) =>
        SafeArgumentPattern().IsMatch(value) ? value : Quote(value);

    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    [GeneratedRegex("^[A-Za-z0-9_./:\\\\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeArgumentPattern();
}
