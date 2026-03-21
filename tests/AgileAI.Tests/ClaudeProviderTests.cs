using System.Net;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Providers.Claude;

namespace AgileAI.Tests;

public class ClaudeProviderTests
{
    [Fact]
    public void ClaudeChatModelProvider_ProviderName_ShouldBeClaude()
    {
        var httpClient = new HttpClient();
        var options = new ClaudeOptions { ApiKey = "test-key" };
        var provider = new ClaudeChatModelProvider(httpClient, options);

        Assert.Equal("claude", provider.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_WithInvalidApiKey_ShouldReturnError()
    {
        var httpClient = new HttpClient();
        var options = new ClaudeOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new ClaudeChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "claude-3-5-sonnet-20241022",
            Messages = [ChatMessage.User("test")]
        };

        var response = await provider.CompleteAsync(request);

        Assert.False(response.IsSuccess);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task StreamAsync_WithInvalidApiKey_ShouldReturnError()
    {
        var httpClient = new HttpClient();
        var options = new ClaudeOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new ClaudeChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "claude-3-5-sonnet-20241022",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(request))
        {
            updates.Add(update);
            if (update is ErrorUpdate)
            {
                break;
            }
        }

        Assert.NotEmpty(updates);
        Assert.IsType<ErrorUpdate>(updates[0]);
    }

    [Fact]
    public async Task CompleteAsync_WithValidResponse_ShouldMapCorrectly()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                content = new[]
                {
                    new { type = "text", text = "Hello from Claude!" }
                },
                stopReason = "end_turn",
                usage = new { inputTokens = 4, outputTokens = 8 }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new ClaudeOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new ClaudeChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "claude-3-5-sonnet-20241022",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello from Claude!", result.Message?.TextContent);
        Assert.NotNull(result.Usage);
        Assert.Equal(4, result.Usage.PromptTokens);
        Assert.Equal(8, result.Usage.CompletionTokens);
        Assert.Equal(12, result.Usage.TotalTokens);
    }

    [Fact]
    public async Task CompleteAsync_WithNullResponse_ShouldReturnError()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("null", Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new ClaudeOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new ClaudeChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "claude-3-5-sonnet-20241022",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid response", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_WithSystemMessage_ShouldMapToSystemParameter()
    {
        var capturedRequestContent = string.Empty;
        var fakeHandler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            if (request.Content != null)
            {
                capturedRequestContent = await request.Content.ReadAsStringAsync(ct);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                content = new[] { new { type = "text", text = "Response" } },
                stopReason = "end_turn"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new ClaudeOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new ClaudeChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "claude-3-5-sonnet-20241022",
            Messages =
            [
                new ChatMessage { Role = ChatRole.System, TextContent = "You are helpful" },
                ChatMessage.User("Hello")
            ]
        };

        await provider.CompleteAsync(chatRequest);

        Assert.Contains("\"system\":\"You are helpful\"", capturedRequestContent);
    }

    [Fact]
    public async Task CompleteAsync_WithImageContentPart_ShouldFallBackToTextMarker()
    {
        var capturedRequestContent = string.Empty;
        var fakeHandler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            if (request.Content != null)
            {
                capturedRequestContent = await request.Content.ReadAsStringAsync(ct);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                content = new[] { new { type = "text", text = "Response" } },
                stopReason = "end_turn"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new ClaudeOptions { ApiKey = "test-key" };
        var provider = new ClaudeChatModelProvider(new HttpClient(fakeHandler), options);

        await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "claude-3-5-sonnet-20241022",
            Messages =
            [
                ChatMessage.User(new TextPart("Describe this image"), new ImageUrlPart("https://example.com/cat.png"))
            ]
        });

        Assert.Contains("Describe this image", capturedRequestContent);
        Assert.Contains("[image: https://example.com/cat.png]", capturedRequestContent);
    }
}
