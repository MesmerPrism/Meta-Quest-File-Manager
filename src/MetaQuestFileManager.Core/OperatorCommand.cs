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
    InstallApk
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
        bool overwrite = false)
    {
        Kind = kind;
        CliArguments = new ReadOnlyCollection<string>(cliArguments.ToArray());
        Serial = serial;
        RemotePath = remotePath;
        LocalPath = localPath;
        PackageName = packageName;
        InstallOptions = installOptions;
        Overwrite = overwrite;
    }

    public OperatorCommandKind Kind { get; }

    public IReadOnlyList<string> CliArguments { get; }

    public string? Serial { get; }

    public string? RemotePath { get; }

    public string? LocalPath { get; }

    public string? PackageName { get; }

    public ApkInstallOptions? InstallOptions { get; }

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
}

public sealed record OperatorExecutionResult(
    OperatorCommand Command,
    IReadOnlyList<QuestDevice>? Devices = null,
    IReadOnlyList<RemoteEntry>? RemoteEntries = null,
    IReadOnlyList<string>? Packages = null,
    CommandResult? CommandResult = null,
    ApkExportResult? ApkExportResult = null);

public sealed class OperatorCommandExecutor(AdbClient client)
{
    private readonly AdbClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<OperatorExecutionResult> ExecuteAsync(
        OperatorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
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

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown operator command.");
        }
    }

    private static string Require(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"The operator command is missing {name}.");
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
