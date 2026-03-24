using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.Extensions.FileSystem;

public class FileSystemToolRegistryFactory(
    ListDirectoryTool listDirectoryTool,
    SearchFilesTool searchFilesTool,
    ReadFileTool readFileTool,
    ReadFilesBatchTool readFilesBatchTool,
    WriteFileTool writeFileTool)
{
    public IToolRegistry CreateDefaultRegistry()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register([listDirectoryTool, searchFilesTool, readFileTool, readFilesBatchTool, writeFileTool]);
        return registry;
    }
}
