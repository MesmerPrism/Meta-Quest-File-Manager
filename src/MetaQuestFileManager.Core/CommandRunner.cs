using System.Diagnostics;
using System.Text;

namespace MetaQuestFileManager.Core;

public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {fileName}.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not start {fileName}. Verify the configured ADB path.",
                exception);
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var outputTask = process.StandardOutput.ReadToEndAsync(linkedSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"{Path.GetFileName(fileName)} timed out after {timeout}.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var standardOutput = await outputTask.ConfigureAwait(false);
        var standardError = await errorTask.ConfigureAwait(false);
        stopwatch.Stop();

        return new CommandResult(
            fileName,
            arguments.ToArray(),
            process.ExitCode,
            standardOutput,
            standardError,
            stopwatch.Elapsed);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cleanup must not hide the original timeout or cancellation.
        }
    }
}
