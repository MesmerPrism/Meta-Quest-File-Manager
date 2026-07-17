namespace MetaQuestFileManager.Core;

public static class AdbLocator
{
    public const string EnvironmentVariable = "META_QUEST_FILE_MANAGER_ADB";

    public static string? Find(string? explicitPath = null)
    {
        var candidates = new List<string?>
        {
            explicitPath,
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android",
                "Sdk",
                "platform-tools",
                AdbFileName),
            FromSdkRoot(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")),
            FromSdkRoot(Environment.GetEnvironmentVariable("ANDROID_HOME")),
            FindOnPath(AdbFileName),
            OperatingSystem.IsWindows() ? null : FindOnPath("adb")
        };

        return candidates.FirstOrDefault(
            static candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }

    public static string FindOrThrow(string? explicitPath = null) =>
        Find(explicitPath) ?? throw new InvalidOperationException(
            "ADB was not found. Install Android SDK Platform Tools, put adb on PATH, " +
            $"or set {EnvironmentVariable} to the executable path.");

    private static string AdbFileName => OperatingSystem.IsWindows() ? "adb.exe" : "adb";

    private static string? FromSdkRoot(string? sdkRoot) =>
        string.IsNullOrWhiteSpace(sdkRoot)
            ? null
            : Path.Combine(sdkRoot, "platform-tools", AdbFileName);

    private static string? FindOnPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(entry.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
