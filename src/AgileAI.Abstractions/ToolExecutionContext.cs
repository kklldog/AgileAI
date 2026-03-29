namespace AgileAI.Abstractions;

public record ToolExecutionContext
{
    public ToolExecutionContext()
    {
    }

    public ToolExecutionContext(ToolCall toolCall)
    {
        ToolCall = toolCall;
    }

    public ToolCall ToolCall { get; init; } = null!;
    public IReadOnlyList<ChatMessage> ChatHistory { get; init; } = [];
    public IServiceProvider? ServiceProvider { get; init; }
    public string? SessionId { get; init; }
    public string? ConversationId { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}
