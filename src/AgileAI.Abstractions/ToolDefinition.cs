namespace AgileAI.Abstractions;

public record ToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public object? ParametersSchema { get; init; }
    public ToolApprovalMode ApprovalMode { get; init; } = ToolApprovalMode.None;
}
