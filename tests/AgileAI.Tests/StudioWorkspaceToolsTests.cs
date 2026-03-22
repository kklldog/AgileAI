using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;

namespace AgileAI.Tests;

public class StudioWorkspaceToolsTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly FileSystemPathGuard _pathGuard;
    private readonly FileSystemToolOptions _options;

    public StudioWorkspaceToolsTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"agileai-studio-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        _options = new FileSystemToolOptions
        {
            RootPath = _workspaceRoot
        };
        _pathGuard = new FileSystemPathGuard(_options);
    }

    [Fact]
    public void ResolvePath_ShouldRejectPathOutsideWorkspace()
    {
        Assert.Throws<InvalidOperationException>(() => _pathGuard.ResolvePath("../outside.txt"));
    }

    [Fact]
    public async Task ReadFileTool_ShouldReturnFileContents()
    {
        var filePath = Path.Combine(_workspaceRoot, "README.md");
        await File.WriteAllTextAsync(filePath, "Hello from workspace");
        var tool = new ReadFileTool(_pathGuard, _options);

        var result = await tool.ExecuteAsync(CreateContext("read_file", "{\"path\":\"README.md\"}"));

        Assert.Contains("Path: README.md", result.Content);
        Assert.Contains("Hello from workspace", result.Content);
    }

    [Fact]
    public async Task WriteFileTool_ShouldCreateWorkspaceFile()
    {
        var tool = new WriteFileTool(_pathGuard);

        var result = await tool.ExecuteAsync(CreateContext("write_file", "{\"path\":\"notes/output.txt\",\"content\":\"written by test\"}"));

        Assert.True(File.Exists(Path.Combine(_workspaceRoot, "notes", "output.txt")));
        Assert.Contains("notes/output.txt", result.Content);
        Assert.Equal("written by test", await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "notes", "output.txt")));
    }

    [Fact]
    public async Task ListDirectoryTool_ShouldReturnWorkspaceEntries()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "README.md"), "hi");
        var tool = new ListDirectoryTool(_pathGuard);

        var result = await tool.ExecuteAsync(CreateContext("list_directory", "{\"path\":\".\"}"));

        Assert.Contains("docs/", result.Content);
        Assert.Contains("README.md", result.Content);
    }

    [Fact]
    public async Task RegisterFileSystemTools_ShouldAddDefaultToolsToRegistry()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "README.md"), "Hello from registry");
        var registry = new InMemoryToolRegistry()
            .RegisterFileSystemTools(new FileSystemToolOptions { RootPath = _workspaceRoot });

        var found = registry.TryGetTool("read_file", out var tool);

        Assert.True(found);
        Assert.NotNull(tool);
        var result = await tool!.ExecuteAsync(CreateContext("read_file", "{\"path\":\"README.md\"}"));
        Assert.Contains("Hello from registry", result.Content);
    }

    [Fact]
    public async Task SearchFilesTool_ShouldReturnMatchingFiles()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "docs", "guide.md"), "AgileAI.Studio filesystem tools");
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "notes.txt"), "nothing interesting here");
        var tool = new SearchFilesTool(_pathGuard);

        var result = await tool.ExecuteAsync(CreateContext("search_files", "{\"path\":\".\",\"query\":\"AgileAI.Studio\",\"limit\":5}"));

        Assert.Contains("docs/guide.md", result.Content);
        Assert.DoesNotContain("notes.txt", result.Content);
    }

    private static ToolExecutionContext CreateContext(string toolName, string arguments)
        => new()
        {
            ToolCall = new ToolCall
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = toolName,
                Arguments = arguments
            }
        };

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

}
