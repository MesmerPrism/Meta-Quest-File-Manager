using System.Security.Cryptography;

namespace MetaQuestFileManager.Core;

public sealed class AdbClient
{
    private static readonly TimeSpan InspectionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);
    private readonly ICommandRunner _runner;

    public AdbClient(string adbPath, ICommandRunner? runner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        AdbPath = adbPath;
        _runner = runner ?? new CommandRunner();
    }

    public string AdbPath { get; }

    public static AdbClient CreateDefault(string? explicitAdbPath = null, ICommandRunner? runner = null) =>
        new(AdbLocator.FindOrThrow(explicitAdbPath), runner);

    public async Task<IReadOnlyList<QuestDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            new[] { "devices", "-l" },
            InspectionTimeout,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("ADB device discovery");
        return AdbOutputParser.ParseDevices(result.StandardOutput);
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListRemoteDirectoryAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        remotePath = AndroidInput.RequireRemotePath(remotePath);
        var command = $"ls -1Ap -- {AndroidInput.ShellQuote(remotePath)}";
        var result = await RunForDeviceAsync(
            serial,
            new[] { "shell", command },
            InspectionTimeout,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess($"List {remotePath}");
        return AdbOutputParser.ParseRemoteDirectory(remotePath, result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> GetThirdPartyPackageNamesAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        var result = await RunForDeviceAsync(
            serial,
            new[] { "shell", "pm list packages -3" },
            InspectionTimeout,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("List third-party packages");
        return AdbOutputParser.ParsePackageNames(result.StandardOutput);
    }

    public async Task<QuestPackage> InspectPackageAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        packageName = AndroidInput.RequirePackageName(packageName);
        var result = await RunForDeviceAsync(
            serial,
            new[] { "shell", $"pm path {AndroidInput.ShellQuote(packageName)}" },
            InspectionTimeout,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess($"Inspect package {packageName}");

        var paths = AdbOutputParser.ParsePackagePaths(result.StandardOutput);
        if (paths.Count == 0)
        {
            throw new InvalidOperationException(
                $"Android did not report an installed APK path for {packageName}.");
        }

        return new QuestPackage(packageName, paths);
    }

    public async Task<CommandResult> PullFileAsync(
        string serial,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        remotePath = AndroidInput.RequireRemotePath(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        var fullLocalPath = Path.GetFullPath(localPath);
        var parent = Path.GetDirectoryName(fullLocalPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var result = await RunForDeviceAsync(
            serial,
            new[] { "pull", remotePath, fullLocalPath },
            TransferTimeout,
            cancellationToken).ConfigureAwait(false);
        return result.EnsureSuccess($"Pull {remotePath}");
    }

    public async Task<CommandResult> PushFileAsync(
        string serial,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        remotePath = AndroidInput.RequireRemotePath(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        var fullLocalPath = Path.GetFullPath(localPath);
        if (!File.Exists(fullLocalPath))
        {
            throw new FileNotFoundException("The local file to push was not found.", fullLocalPath);
        }

        var result = await RunForDeviceAsync(
            serial,
            new[] { "push", fullLocalPath, remotePath },
            TransferTimeout,
            cancellationToken).ConfigureAwait(false);
        return result.EnsureSuccess($"Push {Path.GetFileName(fullLocalPath)}");
    }

    public async Task<CommandResult> InstallApkAsync(
        string serial,
        string apkPath,
        ApkInstallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        var fullApkPath = Path.GetFullPath(apkPath);
        if (!File.Exists(fullApkPath))
        {
            throw new FileNotFoundException("The APK to install was not found.", fullApkPath);
        }

        if (!string.Equals(Path.GetExtension(fullApkPath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The install input must be an .apk file.", nameof(apkPath));
        }

        var arguments = CreateInstallArguments("install", options);
        arguments.Add(fullApkPath);
        var result = await RunForDeviceAsync(
            serial,
            arguments,
            TransferTimeout,
            cancellationToken).ConfigureAwait(false);
        return result.EnsureSuccess($"Install {Path.GetFileName(fullApkPath)}");
    }

    public async Task<ApkBundleInstallResult> InstallApkBundleAsync(
        string serial,
        IReadOnlyList<string> apkPaths,
        ApkInstallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        serial = AndroidInput.RequireSerial(serial);
        ArgumentNullException.ThrowIfNull(apkPaths);
        if (apkPaths.Count < 2)
        {
            throw new InvalidDataException(
                "An APK bundle install requires at least two APK files.");
        }

        var normalizedPaths = apkPaths.Select(ValidateInstallApkPath).ToArray();
        if (normalizedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedPaths.Length)
        {
            throw new InvalidDataException("The APK bundle contains the same APK path more than once.");
        }

        var arguments = CreateInstallArguments("install-multiple", options);
        arguments.AddRange(normalizedPaths);
        var result = await RunForDeviceAsync(
            serial,
            arguments,
            TransferTimeout,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess($"Install APK bundle ({normalizedPaths.Length} parts)");
        return new ApkBundleInstallResult(normalizedPaths, result);
    }

    public async Task<ApkExportResult> ExportSingleApkAsync(
        string serial,
        string packageName,
        string outputPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var package = await InspectPackageAsync(serial, packageName, cancellationToken)
            .ConfigureAwait(false);
        if (package.ApkPaths.Count != 1)
        {
            throw new SplitPackageException(package.PackageName, package.ApkPaths);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullOutputPath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The export destination must end in .apk.", nameof(outputPath));
        }

        var checksumPath = fullOutputPath + ".sha256";
        if (!overwrite && (File.Exists(fullOutputPath) || File.Exists(checksumPath)))
        {
            throw new IOException("The APK or checksum destination already exists. Choose another path or allow overwrite.");
        }

        await PullFileAsync(
            serial,
            package.ApkPaths[0],
            fullOutputPath,
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(fullOutputPath))
        {
            throw new IOException("ADB reported success but the exported APK was not created.");
        }

        var sha256 = await ComputeSha256Async(fullOutputPath, cancellationToken).ConfigureAwait(false);
        var checksumLine = $"{sha256.ToLowerInvariant()}  {Path.GetFileName(fullOutputPath)}{Environment.NewLine}";
        await File.WriteAllTextAsync(checksumPath, checksumLine, cancellationToken).ConfigureAwait(false);

        return new ApkExportResult(
            package.PackageName,
            package.ApkPaths[0],
            fullOutputPath,
            checksumPath,
            sha256,
            new FileInfo(fullOutputPath).Length);
    }

    private Task<CommandResult> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var scoped = new List<string> { "-s", AndroidInput.RequireSerial(serial) };
        scoped.AddRange(arguments);
        return RunAsync(scoped, timeout, cancellationToken);
    }

    private static List<string> CreateInstallArguments(
        string installCommand,
        ApkInstallOptions? options)
    {
        options ??= new ApkInstallOptions();
        var arguments = new List<string> { installCommand };
        if (options.ReplaceExisting)
        {
            arguments.Add("-r");
        }

        if (options.AllowDowngrade)
        {
            arguments.Add("-d");
        }

        if (options.GrantRuntimePermissions)
        {
            arguments.Add("-g");
        }

        if (options.AllowTestPackages)
        {
            arguments.Add("-t");
        }

        return arguments;
    }

    private static string ValidateInstallApkPath(string apkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        var fullApkPath = Path.GetFullPath(apkPath);
        if (!File.Exists(fullApkPath))
        {
            throw new FileNotFoundException("An APK bundle part was not found.", fullApkPath);
        }

        if (!string.Equals(Path.GetExtension(fullApkPath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Every APK bundle input must end in .apk: {fullApkPath}",
                nameof(apkPath));
        }

        return fullApkPath;
    }

    private Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(AdbPath, arguments, timeout, cancellationToken);

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
