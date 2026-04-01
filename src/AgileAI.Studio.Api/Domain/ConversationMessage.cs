namespace AgileAI.Studio.Api.Domain;

public class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public string? FinishReason { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? AppliedSkillName { get; set; }
    public string? AppliedToolNamesJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
