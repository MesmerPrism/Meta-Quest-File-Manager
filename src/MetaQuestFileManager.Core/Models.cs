using System.Collections.ObjectModel;

namespace MetaQuestFileManager.Core;

public sealed record CommandResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;

    public string CondensedOutput
    {
        get
        {
            var output = string.Join(
                Environment.NewLine,
                new[] { StandardError.Trim(), StandardOutput.Trim() }
                    .Where(static value => value.Length > 0));
            return output.Length == 0 ? $"Command exited with code {ExitCode}." : output;
        }
    }

    public CommandResult EnsureSuccess(string operation)
    {
        if (!Succeeded)
        {
            throw new AdbCommandException(operation, this);
        }

        return this;
    }
}

public sealed record QuestDevice(
    string Serial,
    string State,
    string? Model,
    string? Product)
{
    public bool IsReady => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);

    public string DisplayName
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} — {Serial}";
            return IsReady ? label : $"{label} ({State})";
        }
    }
}

public sealed record RemoteEntry(string Name, string FullPath, bool IsDirectory)
{
    public string TypeLabel => IsDirectory ? "Folder" : "File";
}

public sealed record QuestPackage(string PackageName, IReadOnlyList<string> ApkPaths)
{
    public bool IsSplitPackage => ApkPaths.Count > 1;
}

public sealed record ApkInstallOptions(
    bool ReplaceExisting = true,
    bool AllowDowngrade = false,
    bool GrantRuntimePermissions = false,
    bool AllowTestPackages = false);

public sealed record ApkExportResult(
    string PackageName,
    string SourcePath,
    string OutputPath,
    string ChecksumPath,
    string Sha256,
    long SizeBytes);

public sealed class AdbCommandException : InvalidOperationException
{
    public AdbCommandException(string operation, CommandResult result)
        : base($"{operation} failed: {result.CondensedOutput}")
    {
        Result = result;
    }

    public CommandResult Result { get; }
}

public sealed class SplitPackageException : InvalidOperationException
{
    public SplitPackageException(string packageName, IReadOnlyList<string> apkPaths)
        : base(
            $"{packageName} is installed as {apkPaths.Count} APK parts. " +
            "Single-APK export was refused so the backup cannot be incomplete.")
    {
        PackageName = packageName;
        ApkPaths = new ReadOnlyCollection<string>(apkPaths.ToArray());
    }

    public string PackageName { get; }

    public IReadOnlyList<string> ApkPaths { get; }
}
