using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public static class FileSystemToolRegistryExtensions
{
    public static IToolRegistry RegisterFileSystemTools(this IToolRegistry registry, Action<FileSystemToolOptions> configure)
    {
        var options = new FileSystemToolOptions();
        configure(options);
        return registry.RegisterFileSystemTools(options);
    }

    public static IToolRegistry RegisterFileSystemTools(this IToolRegistry registry, FileSystemToolOptions options)
    {
        var pathGuard = new FileSystemPathGuard(options);
        registry.Register([
            new ListDirectoryTool(pathGuard),
            new SearchFilesTool(pathGuard),
            new ReadFileTool(pathGuard, options),
            new ReadFilesBatchTool(pathGuard, options),
            new WriteFileTool(pathGuard),
            new CreateDirectoryTool(pathGuard),
            new MoveFileTool(pathGuard),
            new PatchFileTool(pathGuard),
            new DeleteFileTool(pathGuard, options),
            new DeleteDirectoryTool(pathGuard)
        ]);
        return registry;
    }
}
