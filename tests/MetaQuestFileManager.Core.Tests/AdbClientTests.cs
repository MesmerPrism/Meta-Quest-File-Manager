using System.Security.Cryptography;
using MetaQuestFileManager.Core;

namespace MetaQuestFileManager.Core.Tests;

public sealed class AdbClientTests
{
    [Fact]
    public async Task ListRemoteDirectory_UsesSerialScopedAdbAndQuotedPath()
    {
        var runner = new RecordingCommandRunner(
            (_, _) => Success("Folder/\nfile with spaces.txt\n"));
        var client = new AdbClient("adb-test", runner);

        var entries = await client.ListRemoteDirectoryAsync("QUEST123", "/sdcard/My Files");

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "-s", "QUEST123", "shell", "ls -1Ap -- '/sdcard/My Files'" }, runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task InstallApk_ProjectsOnlyExplicitOptions()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mqfm-{Guid.NewGuid():N}.apk");
        await File.WriteAllBytesAsync(tempFile, [1, 2, 3]);

        try
        {
            var runner = new RecordingCommandRunner((_, _) => Success("Success\n"));
            var client = new AdbClient("adb-test", runner);

            await client.InstallApkAsync(
                "QUEST123",
                tempFile,
                new ApkInstallOptions(
                    ReplaceExisting: true,
                    AllowDowngrade: true,
                    GrantRuntimePermissions: false,
                    AllowTestPackages: true));

            Assert.Equal(
                new[] { "-s", "QUEST123", "install", "-r", "-d", "-t", Path.GetFullPath(tempFile) },
                runner.Calls[0].Arguments);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InstallApkBundle_UsesOneSerialScopedInstallMultipleCallForEveryPart()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"mqfm-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var baseApk = Path.Combine(tempRoot, "base.apk");
        var splitApk = Path.Combine(tempRoot, "split_config.arm64_v8a.apk");
        await File.WriteAllBytesAsync(baseApk, [1]);
        await File.WriteAllBytesAsync(splitApk, [2]);

        try
        {
            var runner = new RecordingCommandRunner((_, _) => Success("Success\n"));
            var client = new AdbClient("adb-test", runner);

            var result = await client.InstallApkBundleAsync(
                "QUEST123",
                [baseApk, splitApk],
                new ApkInstallOptions(
                    ReplaceExisting: true,
                    AllowDowngrade: true,
                    GrantRuntimePermissions: true,
                    AllowTestPackages: true));

            Assert.Equal(2, result.ApkPaths.Count);
            Assert.Single(runner.Calls);
            Assert.Equal(
                [
                    "-s", "QUEST123", "install-multiple", "-r", "-d", "-g", "-t",
                    Path.GetFullPath(baseApk), Path.GetFullPath(splitApk)
                ],
                runner.Calls[0].Arguments);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InstallApkBundle_RejectsIncompleteSetBeforeRunningAdb()
    {
        var runner = new RecordingCommandRunner((_, _) => Success("Success\n"));
        var client = new AdbClient("adb-test", runner);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.InstallApkBundleAsync("QUEST123", ["only-one.apk"]));

        Assert.Contains("at least two", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ExportSingleApk_WritesApkAndChecksum()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"mqfm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var outputPath = Path.Combine(tempRoot, "com.example.app.apk");
        var expectedBytes = new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x2a };
        var runner = new RecordingCommandRunner((_, arguments) =>
        {
            if (arguments.Contains("pull", StringComparer.Ordinal))
            {
                File.WriteAllBytes(arguments[^1], expectedBytes);
                return Success("1 file pulled\n");
            }

            return Success("package:/data/app/example/base.apk\n");
        });
        var client = new AdbClient("adb-test", runner);

        try
        {
            var result = await client.ExportSingleApkAsync(
                "QUEST123",
                "com.example.app",
                outputPath);

            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(outputPath));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expectedBytes)), result.Sha256);
            Assert.Contains(result.Sha256.ToLowerInvariant(), await File.ReadAllTextAsync(result.ChecksumPath), StringComparison.Ordinal);
            Assert.All(runner.Calls, static call => Assert.Equal("QUEST123", call.Arguments[1]));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExportSingleApk_RejectsSplitPackageBeforePull()
    {
        var runner = new RecordingCommandRunner((_, _) => Success("""
            package:/data/app/example/base.apk
            package:/data/app/example/split_config.arm64_v8a.apk
            """));
        var client = new AdbClient("adb-test", runner);

        var exception = await Assert.ThrowsAsync<SplitPackageException>(
            () => client.ExportSingleApkAsync(
                "QUEST123",
                "com.example.app",
                Path.Combine(Path.GetTempPath(), $"mqfm-{Guid.NewGuid():N}.apk")));

        Assert.Equal(2, exception.ApkPaths.Count);
        Assert.Single(runner.Calls);
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
