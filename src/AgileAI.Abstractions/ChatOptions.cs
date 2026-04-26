namespace AgileAI.Abstractions;

public record ChatOptions
{
    public double? Temperature { get; init; }
    public double? TopP { get; init; }
    public int? MaxTokens { get; init; }
    public string? ThinkingIntensity { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public object? ProviderOptions { get; init; }
}
