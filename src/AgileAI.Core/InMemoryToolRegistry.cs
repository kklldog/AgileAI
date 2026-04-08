using AgileAI.Abstractions;

namespace AgileAI.Core;

public class InMemoryToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public void Register(IEnumerable<ITool> tools)
    {
        foreach (var tool in tools)
        {
            Register(tool);
        }
    }

    public bool TryGetTool(string name, out ITool? tool)
    {
        return _tools.TryGetValue(name, out tool);
    }

    public IReadOnlyList<ITool> GetAllTools()
    {
        return _tools.Values.ToList();
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition
        {
            Name = t.Name,
            Description = t.Description,
            ParametersSchema = t.ParametersSchema,
            ApprovalMode = ToolApprovalMetadataResolver.ResolveApprovalMode(t)
        }).ToList();
    }
}
