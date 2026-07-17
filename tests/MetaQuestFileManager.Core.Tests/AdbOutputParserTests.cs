using MetaQuestFileManager.Core;

namespace MetaQuestFileManager.Core.Tests;

public sealed class AdbOutputParserTests
{
    [Fact]
    public void ParseDevices_PreservesReadyAndUnauthorizedRows()
    {
        const string output = """
            List of devices attached
            QUEST123 device product:eureka model:Quest_3 transport_id:2
            192.0.2.10:5555 unauthorized product:eureka model:Quest_3

            """;

        var devices = AdbOutputParser.ParseDevices(output);

        Assert.Equal(2, devices.Count);
        Assert.True(devices[0].IsReady);
        Assert.Equal("Quest_3", devices[0].Model);
        Assert.False(devices[1].IsReady);
        Assert.Contains("unauthorized", devices[1].DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRemoteDirectory_KeepsSpacesAndOrdersFoldersFirst()
    {
        const string output = """
            example file.txt
            Pictures/
            .hidden
            """;

        var entries = AdbOutputParser.ParseRemoteDirectory("/sdcard", output);

        Assert.Equal("Pictures", entries[0].Name);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal("/sdcard/example file.txt", entries[2].FullPath);
    }

    [Fact]
    public void ParsePackagePaths_ReturnsEveryApkPart()
    {
        const string output = """
            package:/data/app/example/base.apk
            package:/data/app/example/split_config.arm64_v8a.apk
            """;

        var paths = AdbOutputParser.ParsePackagePaths(output);

        Assert.Equal(2, paths.Count);
        Assert.EndsWith("base.apk", paths[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ShellQuote_EscapesSingleQuotesWithoutDroppingText()
    {
        var quoted = AndroidInput.ShellQuote("/sdcard/It's here");

        Assert.Equal("'/sdcard/It'\\''s here'", quoted);
    }

    [Fact]
    public void ParseWifiIpv4Address_UsesWlanRouteSourceAddress()
    {
        const string output = """
            192.0.2.0/24 dev wlan0 proto kernel scope link src 192.0.2.42 metric 303
            198.51.100.0/24 dev p2p0 proto kernel scope link src 198.51.100.8
            """;

        var address = AdbOutputParser.ParseWifiIpv4Address(output);

        Assert.Equal("192.0.2.42", address);
    }

    [Fact]
    public void ParseWifiIpv4Address_RejectsMissingWlanAddress()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AdbOutputParser.ParseWifiIpv4Address(
                "198.51.100.0/24 dev p2p0 proto kernel scope link src 198.51.100.8"));

        Assert.Contains("wlan0", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("192.0.2.42:5555", true)]
    [InlineData("quest-lab.local:5555", true)]
    [InlineData("QUEST123", false)]
    [InlineData("127.0.0.1:5555", false)]
    public void WifiEndpointParsing_DistinguishesNetworkAdbSerials(string value, bool expected)
    {
        var parsed = AndroidInput.TryParseWifiEndpoint(value, out _, out _);

        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void OperatorProgress_DerivesOnlyBoundedHonestPercentages()
    {
        var determinate = new OperatorProgress("install", "One finished", 1, 4);
        var overComplete = new OperatorProgress("install", "Finished", 5, 4);
        var indeterminate = new OperatorProgress("copy", "Copying", 0, 0);

        Assert.False(determinate.IsIndeterminate);
        Assert.Equal(25, determinate.Percentage);
        Assert.Equal(100, overComplete.Percentage);
        Assert.True(indeterminate.IsIndeterminate);
        Assert.Equal(0, indeterminate.Percentage);
    }
}
