namespace AgileAI.Abstractions;

public record ToolResult
{
    public string ToolCallId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public bool IsSuccess { get; init; } = true;
    public ToolExecutionStatus Status { get; init; } = ToolExecutionStatus.Completed;
    public string? ApprovalRequestId { get; init; }
    public object? Data { get; init; }
}
