using System.Globalization;
using System.Text.RegularExpressions;

namespace QuestIonAbleFileManager.Core;

public static partial class QuestControlParser
{
    public static QuestControlStatus Parse(
        string batteryOutput,
        string trackingOutput,
        string powerOutput,
        string proximityOutput,
        string cpuLevel,
        string gpuLevel,
        DateTimeOffset capturedAt)
    {
        var batteryLevel = ParseIntegerLine(batteryOutput, "level");
        var batteryState = ParseBatteryState(ParseIntegerLine(batteryOutput, "status"));
        var wakefulness = MatchGroup(WakefulnessRegex(), powerOutput);
        var interactiveText = MatchGroup(InteractiveRegex(), powerOutput);
        var interactive = bool.TryParse(interactiveText, out var parsedInteractive)
            ? parsedInteractive
            : (bool?)null;
        var displayState = MatchGroup(DisplayStateRegex(), powerOutput);
        var proximityState = MatchGroup(ProximityStateRegex(), proximityOutput);
        var stayOn = powerOutput.Contains("mStayOn=true", StringComparison.OrdinalIgnoreCase);
        var autoSleepText = MatchGroup(AutoSleepDisabledRegex(), proximityOutput);
        var autoSleepDisabled = bool.TryParse(autoSleepText, out var parsedAutoSleep)
            ? parsedAutoSleep
            : (bool?)null;
        var keepAwake = stayOn || autoSleepDisabled == true;

        return new QuestControlStatus(
            batteryLevel,
            batteryState,
            ParseControllerPower(trackingOutput),
            wakefulness,
            interactive,
            displayState,
            stayOn,
            autoSleepDisabled,
            keepAwake,
            proximityState,
            cpuLevel.Trim(),
            gpuLevel.Trim(),
            capturedAt);
    }

    public static IReadOnlyList<QuestControllerPower> ParseControllerPower(string output)
    {
        var controllers = new Dictionary<string, QuestControllerPower>(StringComparer.OrdinalIgnoreCase);
        var currentHand = string.Empty;
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Contains("left", StringComparison.OrdinalIgnoreCase))
            {
                currentHand = "Left";
            }
            else if (line.Contains("right", StringComparison.OrdinalIgnoreCase))
            {
                currentHand = "Right";
            }

            var entryIndex = line.IndexOf("[id:", StringComparison.OrdinalIgnoreCase);
            if (currentHand.Length == 0 || entryIndex < 0)
            {
                continue;
            }

            var entry = line[entryIndex..];
            var batteryText = ExtractSegment(entry, "battery:").TrimEnd('%');
            controllers[currentHand] = new QuestControllerPower(
                currentHand,
                int.TryParse(batteryText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var battery)
                    ? battery
                    : null,
                ExtractSegment(entry, "conn:"),
                ExtractSegment(entry, "id:"));
        }

        return controllers.Values
            .OrderBy(static controller => controller.Hand == "Left" ? 0 : 1)
            .ToArray();
    }

    private static int? ParseIntegerLine(string output, string name)
    {
        var match = Regex.Match(
            output,
            $@"(?m)^\s*{Regex.Escape(name)}:\s*(?<value>\d+)\s*$",
            RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : null;
    }

    private static string ParseBatteryState(int? state) => state switch
    {
        2 => "charging",
        3 => "discharging",
        4 => "not charging",
        5 => "full",
        _ => "unknown"
    };

    private static string ExtractSegment(string input, string label)
    {
        var start = input.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += label.Length;
        var end = input.IndexOfAny([',', ']'], start);
        if (end < 0)
        {
            end = input.Length;
        }

        return input[start..end].Trim();
    }

    private static string MatchGroup(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    [GeneratedRegex(@"(?m)^\s*mWakefulness=(?<value>[^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WakefulnessRegex();

    [GeneratedRegex(@"(?m)^\s*mInteractive=(?<value>true|false)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InteractiveRegex();

    [GeneratedRegex(@"(?m)^\s*Display Power:.*?state=(?<value>[^\s\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplayStateRegex();

    [GeneratedRegex(@"Virtual proximity state:\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProximityStateRegex();

    [GeneratedRegex(@"isAutosleepDisabled:\s*(?<value>true|false)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AutoSleepDisabledRegex();
}
