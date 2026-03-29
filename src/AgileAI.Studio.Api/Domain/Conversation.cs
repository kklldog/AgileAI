namespace AgileAI.Studio.Api.Domain;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public AgentDefinition? AgentDefinition { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
    public ICollection<ToolApprovalRequestEntity> ToolApprovalRequests { get; set; } = new List<ToolApprovalRequestEntity>();
}
