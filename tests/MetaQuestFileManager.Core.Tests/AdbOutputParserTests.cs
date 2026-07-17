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
}
