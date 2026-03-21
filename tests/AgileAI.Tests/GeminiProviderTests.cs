using System.Net;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Providers.Gemini;

namespace AgileAI.Tests;

public class GeminiProviderTests
{
    [Fact]
    public void GeminiChatModelProvider_ProviderName_ShouldBeGemini()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-goog-api-key", "test-key");
        var options = new GeminiOptions { ApiKey = "test-key" };
        var provider = new GeminiChatModelProvider(httpClient, options);

        Assert.Equal("gemini", provider.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_WithInvalidApiKey_ShouldReturnError()
    {
        var httpClient = new HttpClient();
        var options = new GeminiOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new GeminiChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "gemini-1.5-flash",
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
        var options = new GeminiOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new GeminiChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "gemini-1.5-flash",
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
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { text = "Hello from Gemini!" } },
                            role = "model"
                        },
                        finishReason = "STOP"
                    }
                },
                usageMetadata = new { promptTokenCount = 3, candidatesTokenCount = 7, totalTokenCount = 10 }
            }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(fakeHandler);
        httpClient.DefaultRequestHeaders.Add("x-goog-api-key", "test-key");
        var options = new GeminiOptions { ApiKey = "test-key" };
        var provider = new GeminiChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gemini-1.5-flash",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello from Gemini!", result.Message?.TextContent);
        Assert.NotNull(result.Usage);
        Assert.Equal(3, result.Usage.PromptTokens);
        Assert.Equal(7, result.Usage.CompletionTokens);
        Assert.Equal(10, result.Usage.TotalTokens);
    }

    [Fact]
    public async Task CompleteAsync_WithNullCandidates_ShouldReturnError()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new { }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(fakeHandler);
        httpClient.DefaultRequestHeaders.Add("x-goog-api-key", "test-key");
        var options = new GeminiOptions { ApiKey = "test-key" };
        var provider = new GeminiChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gemini-1.5-flash",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid response", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_WithBinaryContentPart_ShouldMapInlineData()
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
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { text = "ok" } },
                            role = "model"
                        }
                    }
                }
            }), Encoding.UTF8, "application/json");
            return response;
        });

        var httpClient = new HttpClient(fakeHandler);
        httpClient.DefaultRequestHeaders.Add("x-goog-api-key", "test-key");
        var provider = new GeminiChatModelProvider(httpClient, new GeminiOptions { ApiKey = "test-key" });

        await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "gemini-1.5-flash",
            Messages =
            [
                ChatMessage.User(
                    new TextPart("Describe this image"),
                    new BinaryPart([1, 2, 3, 4], "image/png"))
            ]
        });

        Assert.Contains("inlineData", capturedRequestContent);
        Assert.Contains("image/png", capturedRequestContent);
        Assert.Contains("AQIDBA==", capturedRequestContent);
    }
}
