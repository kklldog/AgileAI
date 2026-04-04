using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgileAI.Studio.Api.Services;

public sealed class ProcessExecutionService
{
    public async Task<ProcessExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        int timeoutMs,
        string? shell,
        CancellationToken cancellationToken)
    {
        var (fileName, arguments, resolvedShell) = ResolveShell(shell, command);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? AppContext.BaseDirectory : workingDirectory!
            }
        };

        var startedAt = DateTimeOffset.UtcNow;
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
            catch
            {
            }
        }

        var stdout = await stdOutTask;
        var stderr = await stdErrTask;
        var completedAt = DateTimeOffset.UtcNow;
        return new ProcessExecutionResult(
            resolvedShell,
            command,
            timedOut ? -1 : process.ExitCode,
            Normalize(stdout),
            Normalize(stderr),
            (int)(completedAt - startedAt).TotalMilliseconds,
            timedOut);
    }

    private static (string FileName, string Arguments, string ResolvedShell) ResolveShell(string? requestedShell, string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (string.Equals(requestedShell, "cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/c {command}", "cmd");
            }

            return ("pwsh", $"-NoLogo -NoProfile -Command {command}", "pwsh");
        }

        if (string.Equals(requestedShell, "sh", StringComparison.OrdinalIgnoreCase) || !File.Exists("/bin/bash"))
        {
            return ("/bin/sh", $"-lc \"{EscapeForPosix(command)}\"", "sh");
        }

        return ("/bin/bash", $"-lc \"{EscapeForPosix(command)}\"", "bash");
    }

    private static string EscapeForPosix(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}

public sealed record ProcessExecutionResult(
    string Shell,
    string Command,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    int DurationMs,
    bool TimedOut);
