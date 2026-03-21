using System.Net;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Providers.OpenAIResponses;

namespace AgileAI.Tests;

public class OpenAIResponsesProviderTests
{
    [Fact]
    public void OpenAIResponsesChatModelProvider_ProviderName_ShouldBeOpenaiResponses()
    {
        var httpClient = new HttpClient();
        var options = new OpenAIResponsesOptions { ApiKey = "test-key" };
        var provider = new OpenAIResponsesChatModelProvider(httpClient, options);

        Assert.Equal("openai-responses", provider.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_WithInvalidApiKey_ShouldReturnError()
    {
        var httpClient = new HttpClient();
        var options = new OpenAIResponsesOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new OpenAIResponsesChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "gpt-4o",
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
        var options = new OpenAIResponsesOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new OpenAIResponsesChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "gpt-4o",
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
                status = "completed",
                output = new[]
                {
                    new
                    {
                        type = "message",
                        content = new[]
                        {
                            new { type = "text", text = "Hello from Responses API!" }
                        }
                    }
                },
                usage = new { input_tokens = 5, output_tokens = 10, total_tokens = 15 }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIResponsesOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIResponsesChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-4o",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello from Responses API!", result.Message?.TextContent);
        Assert.NotNull(result.Usage);
        Assert.Equal(5, result.Usage.PromptTokens);
        Assert.Equal(10, result.Usage.CompletionTokens);
        Assert.Equal(15, result.Usage.TotalTokens);
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

        var options = new OpenAIResponsesOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIResponsesChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-4o",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid response", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_WithContentParts_ShouldFallBackToTextMarkers()
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
                status = "completed",
                output = new[]
                {
                    new
                    {
                        type = "message",
                        content = new[]
                        {
                            new { type = "text", text = "ok" }
                        }
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var provider = new OpenAIResponsesChatModelProvider(new HttpClient(fakeHandler), new OpenAIResponsesOptions { ApiKey = "test-key" });

        await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "gpt-4o",
            Messages =
            [
                ChatMessage.User(new TextPart("Review this file"), new BinaryPart([1, 2, 3], "application/octet-stream"))
            ]
        });

        Assert.Contains("Review this file", capturedRequestContent);
        Assert.Contains("[binary: application/octet-stream, 3 bytes]", capturedRequestContent);
    }
}
