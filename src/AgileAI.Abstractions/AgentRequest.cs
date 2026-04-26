namespace AgileAI.Abstractions;

public record AgentRequest
{
    public string Input { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlyList<ChatMessage>? History { get; init; }
    public ChatOptions? Options { get; init; }
    public bool EnableSkills { get; init; } = true;
    public string? PreferredSkill { get; init; }
    public IReadOnlyList<string>? AllowedSkills { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
