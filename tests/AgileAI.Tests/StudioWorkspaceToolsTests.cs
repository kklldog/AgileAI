using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Services;

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
    public async Task WriteFileTool_ShouldRecoverContentFromUnterminatedJsonString()
    {
        var tool = new WriteFileTool(_pathGuard);

        var result = await tool.ExecuteAsync(CreateContext("write_file", "{\"path\":\"tmp/snake.html\",\"content\":\"<html>\\n<body>snake"));

        Assert.True(result.IsSuccess);
        Assert.Equal("<html>\n<body>snake", await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "tmp", "snake.html")));
    }

    [Fact]
    public void RegisterFileSystemTools_ShouldMarkMutatingOperationsAsApprovalRequired()
    {
        var registry = new InMemoryToolRegistry()
            .RegisterFileSystemTools(new FileSystemToolOptions { RootPath = _workspaceRoot });

        var definitions = registry.GetToolDefinitions();
        var writeFile = Assert.Single(definitions, x => x.Name == "write_file");
        var patchFile = Assert.Single(definitions, x => x.Name == "patch_file");
        var createDirectory = Assert.Single(definitions, x => x.Name == "create_directory");
        var moveFile = Assert.Single(definitions, x => x.Name == "move_file");
        var deleteFile = Assert.Single(definitions, x => x.Name == "delete_file");
        var deleteDirectory = Assert.Single(definitions, x => x.Name == "delete_directory");

        Assert.Equal(ToolApprovalMode.PerExecution, writeFile.ApprovalMode);
        Assert.Equal(ToolApprovalMode.PerExecution, patchFile.ApprovalMode);
        Assert.Equal(ToolApprovalMode.PerExecution, createDirectory.ApprovalMode);
        Assert.Equal(ToolApprovalMode.PerExecution, moveFile.ApprovalMode);
        Assert.Equal(ToolApprovalMode.PerExecution, deleteFile.ApprovalMode);
        Assert.Equal(ToolApprovalMode.PerExecution, deleteDirectory.ApprovalMode);
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

    [Fact]
    public async Task ReadFilesBatchTool_ShouldReturnMultipleFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "README.md"), "root readme");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "docs", "guide.md"), "guide body");
        var tool = new ReadFilesBatchTool(_pathGuard, _options);

        var result = await tool.ExecuteAsync(CreateContext("read_files_batch", "{\"paths\":[\"README.md\",\"docs/guide.md\"]}"));

        Assert.Contains("Path: README.md", result.Content);
        Assert.Contains("root readme", result.Content);
        Assert.Contains("Path: docs/guide.md", result.Content);
        Assert.Contains("guide body", result.Content);
    }

    [Fact]
    public void StudioToolRegistryFactory_ShouldExposeWebFetchTool()
    {
        var webFetchHttpClient = new HttpClient(new FakeHttpMessageHandler((request, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            })));
        var fileSystemFactory = new FileSystemToolRegistryFactory(
            new ListDirectoryTool(_pathGuard),
            new SearchFilesTool(_pathGuard),
            new ReadFileTool(_pathGuard, _options),
            new ReadFilesBatchTool(_pathGuard, _options),
            new WriteFileTool(_pathGuard),
            new CreateDirectoryTool(_pathGuard),
            new MoveFileTool(_pathGuard),
            new PatchFileTool(_pathGuard),
            new DeleteFileTool(_pathGuard, _options),
            new DeleteDirectoryTool(_pathGuard));
        var registryFactory = new StudioToolRegistryFactory(
            fileSystemFactory,
            new RunLocalCommandTool(new AgileAI.Core.ProcessExecutionService()),
            new WebFetchTool(webFetchHttpClient));

        var registry = registryFactory.CreateDefaultRegistry();

        Assert.True(registry.TryGetTool("web_fetch", out var tool));
        Assert.NotNull(tool);
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
