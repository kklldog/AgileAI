namespace AgileAI.Studio.Api.Domain;

public class StudioModel
{
    public Guid Id { get; set; }
    public Guid ProviderConnectionId { get; set; }
    public ProviderConnection? ProviderConnection { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ModelKey { get; set; } = string.Empty;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsTools { get; set; } = true;
    public bool SupportsVision { get; set; }
    public string ThinkingIntensitiesJson { get; set; } = "[]";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<AgentDefinition> Agents { get; set; } = new List<AgentDefinition>();
}
