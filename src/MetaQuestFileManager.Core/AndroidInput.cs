using System.Text.RegularExpressions;

namespace MetaQuestFileManager.Core;

public static partial class AndroidInput
{
    public static string RequireSerial(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (!SerialPattern().IsMatch(serial))
        {
            throw new ArgumentException("The ADB serial contains unsupported characters.", nameof(serial));
        }

        return serial;
    }

    public static string RequirePackageName(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        if (!PackagePattern().IsMatch(packageName))
        {
            throw new ArgumentException("The Android package name is not valid.", nameof(packageName));
        }

        return packageName;
    }

    public static string RequireRemotePath(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        if (!remotePath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("An absolute Android path is required.", nameof(remotePath));
        }

        if (remotePath.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("The Android path contains unsupported control characters.", nameof(remotePath));
        }

        return remotePath.Length > 1 ? remotePath.TrimEnd('/') : remotePath;
    }

    public static string ShellQuote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Shell values cannot contain line breaks or NUL characters.", nameof(value));
        }

        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    public static string CombineRemotePath(string root, string name)
    {
        root = RequireRemotePath(root);
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/'))
        {
            throw new ArgumentException("The remote entry name is not valid.", nameof(name));
        }

        return root == "/" ? "/" + name : root + "/" + name;
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SerialPattern();

    [GeneratedRegex("^[A-Za-z0-9_]+(?:\\.[A-Za-z0-9_]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();
}
