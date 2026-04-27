using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.Extensions.FileSystem;

public class FileSystemToolRegistryFactory(
    ListDirectoryTool listDirectoryTool,
    SearchFilesTool searchFilesTool,
    ReadFileTool readFileTool,
    ReadFilesBatchTool readFilesBatchTool,
    WriteFileTool writeFileTool,
    CreateDirectoryTool createDirectoryTool,
    MoveFileTool moveFileTool,
    PatchFileTool patchFileTool,
    DeleteFileTool deleteFileTool,
    DeleteDirectoryTool deleteDirectoryTool)
{
    public IToolRegistry CreateDefaultRegistry()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register([listDirectoryTool, searchFilesTool, readFileTool, readFilesBatchTool, writeFileTool, createDirectoryTool, moveFileTool, patchFileTool, deleteFileTool, deleteDirectoryTool]);
        return registry;
    }

    public IToolRegistry CreateRegistry(IReadOnlyCollection<string> allowedToolNames)
    {
        if (allowedToolNames.Count == 0)
        {
            return new InMemoryToolRegistry();
        }

        var allowed = new HashSet<string>(allowedToolNames, StringComparer.Ordinal);
        var registry = new InMemoryToolRegistry();
        registry.Register(GetAllTools().Where(tool => allowed.Contains(tool.Name)));
        return registry;
    }

    private IReadOnlyList<ITool> GetAllTools()
        => [listDirectoryTool, searchFilesTool, readFileTool, readFilesBatchTool, writeFileTool, createDirectoryTool, moveFileTool, patchFileTool, deleteFileTool, deleteDirectoryTool];
}
