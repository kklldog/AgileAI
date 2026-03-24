using AgileAI.Abstractions;
using AgileAI.Extensions.FileSystem;

namespace AgileAI.Tests;

public class DeleteFileToolTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileSystemPathGuard _pathGuard;
    private readonly DeleteFileTool _tool;

    public DeleteFileToolTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"agileai-delete-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var options = new FileSystemToolOptions { RootPath = _tempRoot };
        _pathGuard = new FileSystemPathGuard(options);
        _tool = new DeleteFileTool(_pathGuard, options);
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
    public async Task ExecuteAsync_WithExistingFile_SoftDeletes()
    {
        var filePath = "test.txt";
        var fullPath = Path.Combine(_tempRoot, filePath);
        await File.WriteAllTextAsync(fullPath, "content");

        var toolCall = new ToolCall
        {
            Id = "test-1",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\"}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(fullPath));
        Assert.Contains("Recycle Bin", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithForceFlag_PermanentlyDeletes()
    {
        var filePath = "permanent.txt";
        var fullPath = Path.Combine(_tempRoot, filePath);
        await File.WriteAllTextAsync(fullPath, "content");

        var toolCall = new ToolCall
        {
            Id = "test-2",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\",\"force\":true}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(fullPath));
        Assert.Contains("Permanently deleted", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentFile_ThrowsException()
    {
        var toolCall = new ToolCall
        {
            Id = "test-3",
            Name = _tool.Name,
            Arguments = "{\"path\":\"missing.txt\"}"
        };
        var context = new ToolExecutionContext(toolCall);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _tool.ExecuteAsync(context));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithWhitelistedExtensions_AllowsDeletion()
    {
        var options = new FileSystemToolOptions 
        { 
            RootPath = _tempRoot,
            AllowedDeleteExtensions = new[] { ".txt", ".log" }
        };
        var pathGuard = new FileSystemPathGuard(options);
        var tool = new DeleteFileTool(pathGuard, options);

        var filePath = "allowed.txt";
        var fullPath = Path.Combine(_tempRoot, filePath);
        await File.WriteAllTextAsync(fullPath, "content");

        var toolCall = new ToolCall
        {
            Id = "test-4",
            Name = tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\",\"force\":true}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonWhitelistedExtensions_BlocksDeletion()
    {
        var options = new FileSystemToolOptions 
        { 
            RootPath = _tempRoot,
            AllowedDeleteExtensions = new[] { ".txt" }
        };
        var pathGuard = new FileSystemPathGuard(options);
        var tool = new DeleteFileTool(pathGuard, options);

        var filePath = "blocked.cs";
        var fullPath = Path.Combine(_tempRoot, filePath);
        await File.WriteAllTextAsync(fullPath, "content");

        var toolCall = new ToolCall
        {
            Id = "test-5",
            Name = tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\",\"force\":true}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ExecuteAsync(context));
        Assert.Contains("whitelist", ex.Message);
    }
}
