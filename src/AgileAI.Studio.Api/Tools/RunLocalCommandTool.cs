using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Studio.Api.Services;

namespace AgileAI.Studio.Api.Tools;

public sealed class RunLocalCommandTool(ProcessExecutionService processExecutionService) : ITool, IApprovalAwareTool
{
    public string Name => "run_local_command";
    public string Description => "Run a local shell command on the host machine after user approval.";
    public ToolApprovalMode ApprovalMode => ToolApprovalMode.PerExecution;

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            shell = new { type = "string", description = "Optional shell override: auto, pwsh, cmd, bash, or sh." },
            command = new { type = "string", description = "Exact command string to execute." },
            workingDirectory = new { type = "string", description = "Optional working directory for the command." },
            timeoutMs = new { type = "integer", description = "Optional timeout in milliseconds." }
        },
        required = new[] { "command" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<RunLocalCommandRequest>(context.ToolCall.Arguments, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid run_local_command arguments.");

        var result = await processExecutionService.ExecuteAsync(
            request.Command,
            request.WorkingDirectory,
            request.TimeoutMs is > 0 ? request.TimeoutMs.Value : 120000,
            request.Shell,
            cancellationToken);

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            IsSuccess = result.ExitCode == 0 && !result.TimedOut,
            Status = result.ExitCode == 0 && !result.TimedOut ? ToolExecutionStatus.Completed : ToolExecutionStatus.Failed,
            Content = JsonSerializer.Serialize(new
            {
                shell = result.Shell,
                command = result.Command,
                exitCode = result.ExitCode,
                stdout = result.StandardOutput,
                stderr = result.StandardError,
                durationMs = result.DurationMs,
                timedOut = result.TimedOut
            }),
            Data = result
        };
    }

    private sealed record RunLocalCommandRequest(string Command, string? WorkingDirectory, int? TimeoutMs, string? Shell);
}
