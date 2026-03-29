using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Tools;

namespace AgileAI.Studio.Api.Services;

public sealed class StudioToolRegistryFactory(FileSystemToolRegistryFactory fileSystemToolRegistryFactory, RunLocalCommandTool runLocalCommandTool)
{
    public IToolRegistry CreateDefaultRegistry()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(fileSystemToolRegistryFactory.CreateDefaultRegistry().GetAllTools());
        registry.Register(runLocalCommandTool);
        return registry;
    }

    public IToolRegistry CreateRegistry(IReadOnlyCollection<string> allowedToolNames)
    {
        if (allowedToolNames.Count == 0)
        {
            return CreateDefaultRegistry();
        }

        var allowed = new HashSet<string>(allowedToolNames, StringComparer.Ordinal);
        var registry = new InMemoryToolRegistry();
        registry.Register(CreateDefaultRegistry().GetAllTools().Where(tool => allowed.Contains(tool.Name)));
        return registry;
    }
}
