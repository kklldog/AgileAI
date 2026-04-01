namespace AgileAI.Abstractions;

public abstract record ChatTurnStreamUpdate;

public sealed record ChatTurnTextDelta(string Delta) : ChatTurnStreamUpdate;

public sealed record ChatTurnUsage(UsageInfo Usage) : ChatTurnStreamUpdate;

public sealed record ChatTurnModelResponse(ChatResponse Response) : ChatTurnStreamUpdate;

public sealed record ChatTurnPendingApproval(
    ChatResponse Response,
    ToolApprovalRequest PendingApprovalRequest,
    IReadOnlyList<ToolResult>? ToolResults = null,
    IReadOnlyList<string>? ToolNames = null) : ChatTurnStreamUpdate;

public sealed record ChatTurnCompleted(
    ChatResponse Response,
    IReadOnlyList<ToolResult>? ToolResults = null,
    IReadOnlyList<string>? ToolNames = null) : ChatTurnStreamUpdate;

public sealed record ChatTurnError(string ErrorMessage) : ChatTurnStreamUpdate;
