using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;

namespace QuestIonAbleFileManager.Core;

public static class AdbOutputParser
{
    public static IReadOnlyList<QuestDevice> ParseDevices(string output)
    {
        var devices = new List<QuestDevice>();
        foreach (var line in Lines(output).Skip(1))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('*'))
            {
                continue;
            }

            var parts = Regex.Split(trimmed, @"\s+");
            if (parts.Length < 2)
            {
                continue;
            }

            var model = parts.FirstOrDefault(
                static part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase));
            var product = parts.FirstOrDefault(
                static part => part.StartsWith("product:", StringComparison.OrdinalIgnoreCase));

            devices.Add(new QuestDevice(
                parts[0],
                parts[1],
                ValueAfterColon(model),
                ValueAfterColon(product)));
        }

        return devices;
    }

    public static IReadOnlyList<string> ParsePackageNames(string output) =>
        Lines(output)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(static line => line["package:".Length..])
            .Where(static packageName => packageName.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static packageName => packageName, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ParsePackagePaths(string output) =>
        Lines(output)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(static line => line["package:".Length..].Trim())
            .Where(static path => path.StartsWith("/", StringComparison.Ordinal) &&
                                  path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static string ParseWifiIpv4Address(string output)
    {
        foreach (var line in Lines(output))
        {
            if (!Regex.IsMatch(line, @"\bdev\s+wlan0\b", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var match = Regex.Match(
                line,
                @"\bsrc\s+(?<address>\d{1,3}(?:\.\d{1,3}){3})\b",
                RegexOptions.CultureInvariant);
            if (!match.Success ||
                !IPAddress.TryParse(match.Groups["address"].Value, out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork ||
                IPAddress.IsLoopback(address))
            {
                continue;
            }

            return address.ToString();
        }

        throw new InvalidOperationException(
            "The headset did not report a Wi-Fi IPv4 address on wlan0. Connect it to Wi-Fi and try again.");
    }

    public static bool IsSuccessfulWifiConnect(string output, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var normalized = output.Trim();
        return normalized.Contains($"connected to {endpoint}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"already connected to {endpoint}", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<RemoteEntry> ParseRemoteDirectory(string root, string output)
    {
        root = AndroidInput.RequireRemotePath(root);
        var entries = new List<RemoteEntry>();

        foreach (var rawLine in Lines(output))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line is "." or ".." or "./" or "../")
            {
                continue;
            }

            var isDirectory = line.EndsWith("/", StringComparison.Ordinal);
            var name = isDirectory ? line[..^1] : line;
            if (name.Length == 0 || name.Contains('/'))
            {
                continue;
            }

            entries.Add(new RemoteEntry(
                name,
                AndroidInput.CombineRemotePath(root, name),
                isDirectory));
        }

        return entries
            .OrderByDescending(static entry => entry.IsDirectory)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private static string? ValueAfterColon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var index = value.IndexOf(':', StringComparison.Ordinal);
        return index >= 0 && index < value.Length - 1 ? value[(index + 1)..] : null;
    }
}
