namespace AgileAI.Abstractions;

public record ChatMessage
{
    public ChatRole Role { get; init; }
    public string? TextContent { get; init; }
    public IReadOnlyList<ContentPart>? ContentParts { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    public static ChatMessage FromText(ChatRole role, string text) => new()
    {
        Role = role,
        TextContent = text
    };

    public static ChatMessage FromParts(ChatRole role, params ContentPart[] parts) => new()
    {
        Role = role,
        ContentParts = parts
    };

    public static ChatMessage System(string text) => FromText(ChatRole.System, text);
    public static ChatMessage User(string text) => FromText(ChatRole.User, text);
    public static ChatMessage User(params ContentPart[] parts) => FromParts(ChatRole.User, parts);
    public static ChatMessage Assistant(string text) => FromText(ChatRole.Assistant, text);
}
