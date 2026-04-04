using AgileAI.Core;

namespace AgileAI.Tests;

public class ProcessExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithSuccessfulCommand_ShouldCaptureOutputAndNormalizeLineEndings()
    {
        var service = new ProcessExecutionService();

        var result = await service.ExecuteAsync("echo hello; echo world", null, 5000, "sh", CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("sh", result.Shell);
        Assert.Equal("hello\nworld\n", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task ExecuteAsync_WithTimeout_ShouldReturnTimedOutResult()
    {
        var service = new ProcessExecutionService();

        var result = await service.ExecuteAsync("sleep 1", null, 50, "sh", CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("sleep 1", result.Command);
    }
}
