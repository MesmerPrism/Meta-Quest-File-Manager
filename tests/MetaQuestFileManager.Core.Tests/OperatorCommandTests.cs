using MetaQuestFileManager.Core;

namespace MetaQuestFileManager.Core.Tests;

public sealed class OperatorCommandTests
{
    [Fact]
    public void FactoriesExposeExactCliArgumentsUsedByGui()
    {
        var localPath = Path.GetFullPath(Path.Combine("Test Data", "example file.txt"));
        var apkPath = Path.GetFullPath(Path.Combine("Test Data", "example app.apk"));
        var commands = new (OperatorCommand Command, string[] Expected)[]
        {
            (OperatorCommands.DiscoverDevices(), ["devices"]),
            (OperatorCommands.ListFiles("QUEST123", "/sdcard/My Files"),
                ["files", "list", "--serial", "QUEST123", "--path", "/sdcard/My Files"]),
            (OperatorCommands.PullFile("QUEST123", "/sdcard/My Files/example.txt", localPath),
                ["files", "pull", "--serial", "QUEST123", "--remote", "/sdcard/My Files/example.txt", "--output", localPath]),
            (OperatorCommands.PushFile("QUEST123", localPath, "/sdcard/Download/example.txt"),
                ["files", "push", "--serial", "QUEST123", "--file", localPath, "--remote", "/sdcard/Download/example.txt"]),
            (OperatorCommands.ListPackages("QUEST123"),
                ["apk", "list", "--serial", "QUEST123"]),
            (OperatorCommands.ExportApk("QUEST123", "com.example.app", apkPath, overwrite: true),
                ["apk", "export", "--serial", "QUEST123", "--package", "com.example.app", "--output", apkPath, "--overwrite"]),
            (OperatorCommands.InstallApk(
                    "QUEST123",
                    apkPath,
                    new ApkInstallOptions(false, true, true, true)),
                ["apk", "install", "--serial", "QUEST123", "--file", apkPath, "--no-replace", "--downgrade", "--grant-runtime-permissions", "--test-only"])
        };

        foreach (var (command, expected) in commands)
        {
            Assert.Equal(expected, command.CliArguments);
        }
    }

    [Fact]
    public void PowerShellCommandQuotesHumanInputsWithoutChangingArguments()
    {
        var command = OperatorCommands.ListFiles("QUEST123", "/sdcard/It's here");

        var rendered = command.ToPowerShellCommand(
            ".\\meta quest file manager.exe",
            "C:\\Android SDK\\platform-tools\\adb.exe");

        Assert.Equal(
            "& '.\\meta quest file manager.exe' files list --serial QUEST123 --path '/sdcard/It''s here' --adb 'C:\\Android SDK\\platform-tools\\adb.exe'",
            rendered);
    }

    [Fact]
    public async Task ExecutorRunsEveryGuiCommandThroughItsTypedCliContract()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"mqfm-operator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.apk");
        var pullPath = Path.Combine(tempRoot, "pulled.txt");
        var exportPath = Path.Combine(tempRoot, "exported.apk");
        await File.WriteAllBytesAsync(inputPath, [0x50, 0x4b, 0x03, 0x04]);

        var runner = new RecordingCommandRunner((_, arguments) =>
        {
            if (arguments.SequenceEqual(["devices", "-l"]))
            {
                return Success("List of devices attached\nQUEST123 device model:Quest_3\n");
            }

            if (arguments.Contains("pm list packages -3", StringComparer.Ordinal))
            {
                return Success("package:com.example.app\n");
            }

            if (arguments.Any(static value => value.StartsWith("pm path ", StringComparison.Ordinal)))
            {
                return Success("package:/data/app/example/base.apk\n");
            }

            if (arguments.Contains("pull", StringComparer.Ordinal))
            {
                File.WriteAllBytes(arguments[^1], [0x50, 0x4b, 0x03, 0x04]);
                return Success("1 file pulled\n");
            }

            if (arguments.Any(static value => value.StartsWith("ls -1Ap", StringComparison.Ordinal)))
            {
                return Success("Download/\nexample.txt\n");
            }

            return Success("Success\n");
        });
        var executor = new OperatorCommandExecutor(new AdbClient("adb-test", runner));

        try
        {
            var devices = await executor.ExecuteAsync(OperatorCommands.DiscoverDevices());
            var files = await executor.ExecuteAsync(OperatorCommands.ListFiles("QUEST123", "/sdcard"));
            await executor.ExecuteAsync(OperatorCommands.PullFile("QUEST123", "/sdcard/example.txt", pullPath));
            await executor.ExecuteAsync(OperatorCommands.PushFile("QUEST123", inputPath, "/sdcard/Download/input.apk"));
            var packages = await executor.ExecuteAsync(OperatorCommands.ListPackages("QUEST123"));
            var export = await executor.ExecuteAsync(
                OperatorCommands.ExportApk("QUEST123", "com.example.app", exportPath));
            await executor.ExecuteAsync(OperatorCommands.InstallApk(
                "QUEST123",
                inputPath,
                new ApkInstallOptions(true, true, false, true)));

            Assert.Single(devices.Devices!);
            Assert.Equal(2, files.RemoteEntries!.Count);
            Assert.Equal(["com.example.app"], packages.Packages);
            Assert.Equal(exportPath, export.ApkExportResult!.OutputPath);
            Assert.Equal(new[] { "devices", "-l" }, runner.Calls[0].Arguments);
            Assert.All(
                runner.Calls.Skip(1),
                static call => Assert.Equal(new[] { "-s", "QUEST123" }, call.Arguments.Take(2)));
            Assert.Contains(
                runner.Calls,
                static call => call.Arguments.SequenceEqual(
                    ["-s", "QUEST123", "install", "-r", "-d", "-t", call.Arguments[^1]]));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static CommandResult Success(string output) =>
        new("adb-test", Array.Empty<string>(), 0, output, string.Empty, TimeSpan.Zero);

    private sealed class RecordingCommandRunner(
        Func<string, IReadOnlyList<string>, CommandResult> handler) : ICommandRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments.ToArray()));
            var handled = handler(fileName, arguments);
            return Task.FromResult(handled with { FileName = fileName, Arguments = arguments.ToArray() });
        }
    }
}
