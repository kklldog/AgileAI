namespace AgileAI.Studio.Api.Domain;

public class AgentDefinition
{
    public Guid Id { get; set; }
    public Guid StudioModelId { get; set; }
    public StudioModel? StudioModel { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.6d;
    public int MaxTokens { get; set; } = 2048;
    public string? ThinkingIntensity { get; set; }
    public bool EnableSkills { get; set; }
    public bool IsPinned { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
