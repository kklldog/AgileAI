using AgileAI.Abstractions;
using AgileAI.Extensions.FileSystem;

namespace AgileAI.Tests;

public class PatchFileToolTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileSystemPathGuard _pathGuard;
    private readonly PatchFileTool _tool;

    public PatchFileToolTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"agileai-patch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var options = new FileSystemToolOptions { RootPath = _tempRoot };
        _pathGuard = new FileSystemPathGuard(options);
        _tool = new PatchFileTool(_pathGuard);
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
    public async Task ExecuteAsync_WithExistingFile_PatchesFile()
    {
        var filePath = "test.txt";
        var fullPath = Path.Combine(_tempRoot, filePath);
        await File.WriteAllTextAsync(fullPath, "line 1\nline 2\nline 3");

        var toolCall = new ToolCall
        {
            Id = "test-1",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\",\"content\":\"new content\"}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal("new content", await File.ReadAllTextAsync(fullPath));
        Assert.True(File.Exists(fullPath + ".bak"));
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentFile_ThrowsException()
    {
        var toolCall = new ToolCall
        {
            Id = "test-2",
            Name = _tool.Name,
            Arguments = "{\"path\":\"missing.txt\",\"content\":\"test\"}"
        };
        var context = new ToolExecutionContext(toolCall);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _tool.ExecuteAsync(context));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithCreateIfMissing_CreatesFile()
    {
        var filePath = "newfile.txt";
        var fullPath = Path.Combine(_tempRoot, filePath);

        var toolCall = new ToolCall
        {
            Id = "test-3",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\",\"content\":\"hello\",\"create_if_missing\":true}}"
        };
        var context = new ToolExecutionContext(toolCall);

        var result = await _tool.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(fullPath));
        Assert.Equal("hello", await File.ReadAllTextAsync(fullPath));
    }

    [Fact]
    public async Task ExecuteAsync_WithExistingFile_CreatesBackup()
    {
        var filePath = "backup-test.txt";
        var fullPath = Path.Combine(_tempRoot, filePath);
        await File.WriteAllTextAsync(fullPath, "original content");

        var toolCall = new ToolCall
        {
            Id = "test-4",
            Name = _tool.Name,
            Arguments = $"{{\"path\":\"{filePath}\",\"content\":\"updated content\"}}"
        };
        var context = new ToolExecutionContext(toolCall);

        await _tool.ExecuteAsync(context);

        Assert.True(File.Exists(fullPath + ".bak"));
        Assert.Equal("original content", await File.ReadAllTextAsync(fullPath + ".bak"));
    }
}
