namespace AgileAI.Abstractions;

public record ChatTurnResult
{
    public ChatResponse Response { get; init; } = null!;
    public IReadOnlyList<ToolResult>? ToolResults { get; init; }
    public ToolApprovalRequest? PendingApprovalRequest { get; init; }
}
