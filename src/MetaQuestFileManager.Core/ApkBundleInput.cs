using System.Collections.ObjectModel;

namespace MetaQuestFileManager.Core;

public sealed record ApkBundleInput
{
    private ApkBundleInput(string folderPath, IReadOnlyList<string> apkPaths)
    {
        FolderPath = folderPath;
        ApkPaths = apkPaths;
    }

    public string FolderPath { get; }

    public IReadOnlyList<string> ApkPaths { get; }

    public static ApkBundleInput FromFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullFolderPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolderPath))
        {
            throw new DirectoryNotFoundException(
                $"The APK bundle folder was not found: {fullFolderPath}");
        }

        var apkPaths = Directory
            .EnumerateFiles(fullFolderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(static path =>
                string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => IsBaseApkName(Path.GetFileName(path)) ? 0 : 1)
            .ThenBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .ToArray();

        if (apkPaths.Length < 2)
        {
            throw new InvalidDataException(
                "An APK bundle folder must contain at least two top-level .apk files. " +
                "Use the single-APK install action when the folder contains only one APK.");
        }

        return new ApkBundleInput(
            fullFolderPath,
            new ReadOnlyCollection<string>(apkPaths));
    }

    private static bool IsBaseApkName(string fileName) =>
        string.Equals(fileName, "base.apk", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "base-master.apk", StringComparison.OrdinalIgnoreCase);
}
