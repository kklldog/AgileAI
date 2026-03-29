namespace AgileAI.Studio.Api.Contracts;

public record ToolApprovalDto(
    Guid Id,
    Guid ConversationId,
    Guid AssistantMessageId,
    string ApprovalRequestId,
    string ToolCallId,
    string ToolName,
    string ArgumentsJson,
    string Status,
    string? DecisionComment,
    string? ResultContent,
    int? ExitCode,
    string? StandardOutput,
    string? StandardError,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public record ResolveToolApprovalRequest(bool Approved, string? Comment);

public record ToolApprovalResolutionResultDto(
    ToolApprovalDto Approval,
    MessageDto AssistantMessage,
    ConversationDto Conversation,
    ToolApprovalDto? PendingApproval);
