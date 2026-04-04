using System.Net;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Studio.Api.Tools;

namespace AgileAI.Tests;

public class WebFetchToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithSuccessfulResponse_ShouldReturnFetchedContent()
    {
        var tool = CreateTool((request, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Hello from web</body></html>")
        }));

        var result = await tool.ExecuteAsync(CreateContext("{\"url\":\"https://example.com\"}"));
        using var payload = JsonDocument.Parse(result.Content);

        Assert.True(result.IsSuccess);
        Assert.Equal(ToolExecutionStatus.Completed, result.Status);
        Assert.Equal("https://example.com/", payload.RootElement.GetProperty("url").GetString());
        Assert.Contains("Hello from web", payload.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeResponse_ShouldTruncateContent()
    {
        var tool = CreateTool((request, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('a', 400))
        }));

        var result = await tool.ExecuteAsync(CreateContext("{\"url\":\"https://example.com\",\"maxCharacters\":256}"));
        using var payload = JsonDocument.Parse(result.Content);

        Assert.True(result.IsSuccess);
        Assert.Contains("[Output truncated to 256 characters]", payload.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithHttpFailure_ShouldReturnFailedResult()
    {
        var tool = CreateTool((request, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("gateway problem")
        }));

        var result = await tool.ExecuteAsync(CreateContext("{\"url\":\"https://example.com\"}"));
        using var payload = JsonDocument.Parse(result.Content);

        Assert.False(result.IsSuccess);
        Assert.Equal(ToolExecutionStatus.Failed, result.Status);
        Assert.Equal(502, payload.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("gateway problem", payload.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithNonHttpScheme_ShouldThrow()
    {
        var tool = CreateTool((request, ct) => throw new InvalidOperationException("should not execute"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.ExecuteAsync(CreateContext("{\"url\":\"file:///tmp/test.txt\"}")));

        Assert.Equal("web_fetch only supports http and https URLs.", error.Message);
    }

    private static WebFetchTool CreateTool(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(new HttpClient(new FakeHttpMessageHandler(handler)));

    private static ToolExecutionContext CreateContext(string arguments)
        => new()
        {
            ToolCall = new ToolCall
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "web_fetch",
                Arguments = arguments
            }
        };
}
