using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.Extensions.FileSystem;

public class FileSystemToolRegistryFactory(
    ListDirectoryTool listDirectoryTool,
    SearchFilesTool searchFilesTool,
    ReadFileTool readFileTool,
    WriteFileTool writeFileTool)
{
    public IToolRegistry CreateDefaultRegistry()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register([listDirectoryTool, searchFilesTool, readFileTool, writeFileTool]);
        return registry;
    }
}
