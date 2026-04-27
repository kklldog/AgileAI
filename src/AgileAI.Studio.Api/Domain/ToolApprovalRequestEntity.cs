namespace AgileAI.Studio.Api.Domain;

public class ToolApprovalRequestEntity
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public Guid AssistantMessageId { get; set; }
    public string ApprovalRequestId { get; set; } = string.Empty;
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = string.Empty;
    public string AssistantToolCallContent { get; set; } = string.Empty;
    public string? AssistantReasoningContent { get; set; }
    public ToolApprovalStatus Status { get; set; } = ToolApprovalStatus.Pending;
    public string? DecisionComment { get; set; }
    public string? ResultContent { get; set; }
    public int? ExitCode { get; set; }
    public string? StandardOutput { get; set; }
    public string? StandardError { get; set; }
    public int ConsecutiveFailureCount { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
