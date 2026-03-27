using AgileAI.Abstractions;
using AgileAI.Extensions.FileSystem;

namespace AgileAI.Tests;

public class DeleteDirectoryToolTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileSystemPathGuard _pathGuard;
    private readonly DeleteDirectoryTool _tool;

    public DeleteDirectoryToolTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"agileai-deletedir-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var options = new FileSystemToolOptions { RootPath = _tempRoot };
        _pathGuard = new FileSystemPathGuard(options);
        _tool = new DeleteDirectoryTool(_pathGuard);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task ExecuteAsync_WithExistingDirectory_SoftDeletes()
    {
        var dirPath = "testdir";
        var fullPath = Path.Combine(_tempRoot, dirPath);
        Directory.CreateDirectory(fullPath);
        await File.WriteAllTextAsync(Path.Combine(fullPath, "file.txt"), "content");

        var toolCall = new ToolCall
        {
            Id = "test-1",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{dirPath}\"}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(fullPath));
        Assert.Contains("Recycle Bin", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithForceFlag_PermanentlyDeletes()
    {
        var dirPath = "permanentdir";
        var fullPath = Path.Combine(_tempRoot, dirPath);
        Directory.CreateDirectory(fullPath);
        await File.WriteAllTextAsync(Path.Combine(fullPath, "file.txt"), "content");

        var toolCall = new ToolCall
        {
            Id = "test-2",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{dirPath}\",\"force\":true}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(fullPath));
        Assert.Contains("Permanently deleted", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentDirectory_ThrowsException()
    {
        var toolCall = new ToolCall
        {
            Id = "test-3",
            Name = _tool.Name,
            Arguments = "{\"path\":\"missingdir\"}"
        };
        var context = new ToolExecutionContext(toolCall);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _tool.ExecuteAsync(context));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithRecursive_DeletesAllContents()
    {
        var dirPath = "nesteddir";
        var fullPath = Path.Combine(_tempRoot, dirPath);
        Directory.CreateDirectory(Path.Combine(fullPath, "subdir1", "subdir2"));
        await File.WriteAllTextAsync(Path.Combine(fullPath, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(fullPath, "subdir1", "file2.txt"), "content2");

        var toolCall = new ToolCall
        {
            Id = "test-4",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{dirPath}\",\"force\":true}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(fullPath));
    }
}
