using QuestIonAbleFileManager.Core;
using System.Security.Cryptography;
using System.Text;

namespace QuestIonAbleFileManager.Core.Tests;

public sealed class RustyKioskIntegrationTests
{
    [Fact]
    public void OperatorResultPreservesCompleteCatalogIncludingNamedMissingApps()
    {
        var result = RustyKioskOperatorResult.Parse(ResultJson(
            requestId: "pc-test",
            command: "status",
            wifiAdbEnabled: false));

        Assert.Equal(2, result.State.Entries.Count);
        var missing = Assert.Single(result.State.Entries, static entry => !entry.Installed);
        Assert.Equal("Purchased Example", missing.Name);
        Assert.Null(missing.PackageName);
        Assert.Equal(["calm", "paid"], missing.Tags);
        Assert.Equal("Not installed", missing.StatusLabel);
        Assert.Contains("paid", result.State.Tags);
    }

    [Fact]
    public void WifiPermissionRequestStaysPendingUntilLaterHeadsetReadbackConfirmsIt()
    {
        var command = OperatorCommands.InvokeRustyKiosk(
            "QUEST123",
            RustyKioskCommand.RequestWifiAdb,
            operatorConfirmed: true);
        var now = DateTimeOffset.UtcNow;
        var pending = new OperatorMutationReceipt(
            "pc-test",
            command.Kind,
            "QUEST123",
            "Rusty Kiosk request-wifi-adb",
            OperatorMutationStage.Pending,
            "Wi-Fi ADB=off",
            HeadsetReadback: true,
            [
                new OperatorMutationTransition(
                    OperatorMutationStage.Sent,
                    now,
                    "Sent"),
                new OperatorMutationTransition(
                    OperatorMutationStage.Pending,
                    now,
                    "Waiting for Meta wearer approval")
            ]);
        var stillOff = new OperatorExecutionResult(
            OperatorCommands.InspectRustyKiosk("QUEST123"),
            RustyKioskOperatorResult: RustyKioskOperatorResult.Parse(ResultJson(
                "status-off",
                "status",
                wifiAdbEnabled: false)));
        var nowOn = new OperatorExecutionResult(
            OperatorCommands.InspectRustyKiosk("QUEST123"),
            RustyKioskOperatorResult: RustyKioskOperatorResult.Parse(ResultJson(
                "status-on",
                "status",
                wifiAdbEnabled: true)));

        var unchanged = OperatorMutationReconciler.Reconcile(pending, command, stillOff);
        var confirmed = OperatorMutationReconciler.Reconcile(unchanged, command, nowOn);

        Assert.Equal(OperatorMutationStage.Pending, unchanged.Stage);
        Assert.Equal(OperatorMutationStage.Confirmed, confirmed.Stage);
        Assert.True(confirmed.HeadsetReadback);
        Assert.Equal(
            [
                OperatorMutationStage.Sent,
                OperatorMutationStage.Pending,
                OperatorMutationStage.Pending,
                OperatorMutationStage.Confirmed
            ],
            confirmed.Transitions.Select(static transition => transition.Stage));
    }

    [Fact]
    public async Task PerformanceMutationRecordsSentPendingConfirmedOnlyAfterGetPropReadback()
    {
        var runner = new RecordingCommandRunner((_, arguments) =>
        {
            if (arguments.Contains("dumpsys", StringComparer.Ordinal) &&
                arguments.Contains("battery", StringComparer.Ordinal))
            {
                return Success("level: 71\nstatus: 3\n");
            }

            if (arguments.Contains("dumpsys", StringComparer.Ordinal) &&
                arguments.Contains("power", StringComparer.Ordinal))
            {
                return Success("mWakefulness=Awake\nmInteractive=true\nDisplay Power: state=ON\n");
            }

            if (arguments.Contains("vrpowermanager", StringComparer.Ordinal))
            {
                return Success("Virtual proximity state: OPEN\n");
            }

            if (arguments.Contains("debug.oculus.cpuLevel", StringComparer.Ordinal))
            {
                return Success("3\n");
            }

            if (arguments.Contains("debug.oculus.gpuLevel", StringComparer.Ordinal))
            {
                return Success("4\n");
            }

            return Success(string.Empty);
        });
        var executor = new OperatorCommandExecutor(new AdbClient("adb-test", runner));
        var progress = new RecordingProgress<OperatorProgress>();

        var execution = await executor.ExecuteAsync(
            OperatorCommands.SetQuestPerformance(
                "QUEST123",
                cpuLevel: 3,
                gpuLevel: 4,
                operatorConfirmed: true),
            progress: progress);

        var receipt = Assert.IsType<OperatorMutationReceipt>(execution.MutationReceipt);
        Assert.Equal(OperatorMutationStage.Confirmed, receipt.Stage);
        Assert.True(receipt.HeadsetReadback);
        Assert.Equal(
            [OperatorMutationStage.Sent, OperatorMutationStage.Pending, OperatorMutationStage.Confirmed],
            receipt.Transitions.Select(static transition => transition.Stage));
        Assert.Contains(
            runner.Calls,
            static call => call.Arguments.Any(argument => argument.Contains("setprop", StringComparison.Ordinal)));
        Assert.Contains(runner.Calls, static call => call.Arguments.Contains("getprop", StringComparer.Ordinal));
        Assert.Contains(progress.Values, static value => value.Stage == "mutation-sent");
        Assert.Contains(progress.Values, static value => value.Stage == "mutation-pending");
        Assert.Contains(progress.Values, static value => value.Stage == "mutation-confirmed");
    }

    [Fact]
    public void MountedProximityDoesNotMasqueradeAsKeepAwake()
    {
        var normal = QuestControlParser.Parse(
            "level: 75\nstatus: 3\n",
            string.Empty,
            "mWakefulness=Awake\nmStayOn=false\n",
            "Virtual proximity state: CLOSE\nisAutosleepDisabled: false\nState: HEADSET_MOUNTED\n",
            string.Empty,
            string.Empty,
            DateTimeOffset.UtcNow);
        var held = QuestControlParser.Parse(
            "level: 75\nstatus: 3\n",
            string.Empty,
            "mWakefulness=Awake\nmStayOn=true\n",
            "Virtual proximity state: CLOSE\nisAutosleepDisabled: true\nState: HEADSET_MOUNTED\n",
            string.Empty,
            string.Empty,
            DateTimeOffset.UtcNow);

        Assert.False(normal.KeepAwakeActive);
        Assert.False(normal.StayOn);
        Assert.False(normal.AutoSleepDisabled);
        Assert.True(held.KeepAwakeActive);
        Assert.True(held.StayOn);
        Assert.True(held.AutoSleepDisabled);
    }

    [Fact]
    public async Task TagFileValidationAllowsNamedNotInstalledEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rusty-kiosk-tags-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schema": "rusty.kiosk.app_tags.v1",
              "apps": [
                { "name": "Purchased Example", "tags": ["paid", "calm"] }
              ]
            }
            """);

        try
        {
            var json = RustyKioskTagFile.ValidateAndRead(path);
            Assert.Contains("Purchased Example", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TagTransferUsesBoundedProviderChunksAndShaInsteadOfRawAndroidDataPaths()
    {
        var entries = string.Join(
            ",\n",
            Enumerable.Range(0, 140).Select(index =>
                $$"""{ "name": "External App {{index:D3}}", "tags": ["group-{{index % 7}}"] }"""));
        var json = $$"""
            {
              "schema": "rusty.kiosk.app_tags.v1",
              "apps": [
                {{entries}}
              ]
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.True(bytes.Length > 6 * 1024);
        var input = Path.Combine(Path.GetTempPath(), $"rusty-kiosk-input-{Guid.NewGuid():N}.json");
        var output = Path.Combine(Path.GetTempPath(), $"rusty-kiosk-output-{Guid.NewGuid():N}.json");
        await File.WriteAllBytesAsync(input, bytes);
        using var received = new MemoryStream();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var runner = new RecordingCommandRunner((_, arguments) =>
        {
            var methodIndex = Array.IndexOf(arguments.ToArray(), "--method");
            var method = methodIndex >= 0 ? arguments[methodIndex + 1] : string.Empty;
            if (method == "tag-write-begin")
            {
                received.SetLength(0);
                return Bundle("accepted=true, completed=true, offset=0, message=ready");
            }

            if (method == "tag-write-chunk")
            {
                var offset = int.Parse(Extra(arguments, "offset:i:"), System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(received.Length, offset);
                var chunk = Convert.FromBase64String(Extra(arguments, "data_base64:s:"));
                received.Write(chunk);
                return Bundle($"accepted=true, completed=true, offset={received.Length}, message=accepted");
            }

            if (method == "tag-write-commit")
            {
                Assert.Equal(bytes, received.ToArray());
                return Bundle($"accepted=true, completed=true, offset={bytes.Length}, message=committed");
            }

            if (method == "tag-read")
            {
                var offset = int.Parse(Extra(arguments, "offset:i:"), System.Globalization.CultureInfo.InvariantCulture);
                var length = Math.Min(6 * 1024, bytes.Length - offset);
                var encoded = Convert.ToBase64String(bytes, offset, length);
                return Bundle(
                    $"accepted=true, completed=true, total_bytes={bytes.Length}, offset={offset}, " +
                    $"sha256={sha}, data_base64={encoded}, message=ready");
            }

            return new CommandResult("adb-test", arguments, 1, string.Empty, "unexpected method", TimeSpan.Zero);
        });
        var client = new AdbClient("adb-test", runner);

        try
        {
            await client.PushRustyKioskTagFileAsync("QUEST123", input);
            await client.PullRustyKioskTagFileAsync("QUEST123", output);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(output));
            Assert.True(runner.Calls.Count(call => call.Arguments.Contains("tag-write-chunk")) >= 2);
            Assert.True(runner.Calls.Count(call => call.Arguments.Contains("tag-read")) >= 2);
            Assert.All(
                runner.Calls,
                static call =>
                {
                    Assert.Equal(["-s", "QUEST123"], call.Arguments.Take(2));
                    Assert.Contains(RustyKioskContract.OperatorUri, call.Arguments);
                    Assert.DoesNotContain("push", call.Arguments);
                    Assert.DoesNotContain(RustyKioskContract.TagFilePath, call.Arguments);
                });
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    private static string ResultJson(string requestId, string command, bool wifiAdbEnabled) => $$"""
        {
          "schema": "rusty.kiosk.cli_result.v1",
          "request_id": "{{requestId}}",
          "command": "{{command}}",
          "accepted": true,
          "completed": true,
          "message": "Complete",
          "state": {
            "installed_count": 1,
            "not_installed_count": 1,
            "visible_count": 2,
            "entries_truncated": false,
            "entries": [
              {
                "key": "package:com.example.installed",
                "name": "Installed Example",
                "package": "com.example.installed",
                "installed": true,
                "launchable": true,
                "tags": ["calm"]
              },
              {
                "key": "name:purchased example",
                "name": "Purchased Example",
                "package": null,
                "installed": false,
                "launchable": false,
                "tags": ["calm", "paid"]
              }
            ],
            "visible_entries_truncated": false,
            "visible_entries": [],
            "search": "",
            "tag_filter": null,
            "status_line": "Ready",
            "tag_file_path": "/sdcard/Android/data/io.github.mesmerprism.rustykiosk/files/tags/app-tags.v1.json",
            "selected_key": null,
            "selected_name": null,
            "selected_package": null,
            "selected_installed": false,
            "selected_launchable": false,
            "wifi_adb_enabled": {{wifiAdbEnabled.ToString().ToLowerInvariant()}},
            "setup_helper_installed": true,
            "setup_helper_ready": true,
            "request_wifi_adb_after_boot": false,
            "accessibility_enabled": false,
            "guard_armed": false,
            "operation_in_progress": null
          }
        }
        """;

    private static CommandResult Success(string output) =>
        new("adb-test", [], 0, output, string.Empty, TimeSpan.Zero);

    private static CommandResult Bundle(string values) =>
        Success($"Result: Bundle[{{{values}}}]\n");

    private static string Extra(IReadOnlyList<string> arguments, string prefix) =>
        arguments.First(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];

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
            var result = handler(fileName, arguments);
            return Task.FromResult(result with { FileName = fileName, Arguments = arguments.ToArray() });
        }
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
