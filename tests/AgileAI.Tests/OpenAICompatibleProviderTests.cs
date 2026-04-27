using System.Net;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Providers.OpenAICompatible;

namespace AgileAI.Tests;

public class OpenAICompatibleProviderTests
{
    [Fact]
    public void OpenAICompatibleChatModelProvider_ProviderName_ShouldComeFromOptions()
    {
        var provider = CreateProvider(new HttpClient(), options =>
        {
            options.ProviderName = "deepseek";
            options.ApiKey = "test-key";
            options.BaseUrl = "https://example.com/v1/";
        });

        Assert.Equal("deepseek", provider.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_WithBearerAuth_ShouldSetAuthorizationHeaderAndUseConfiguredPath()
    {
        string? capturedUrl = null;
        string? authorizationHeader = null;
        string? body = null;

        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            capturedUrl = request.RequestUri?.ToString();
            authorizationHeader = request.Headers.Authorization?.ToString();
            body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);

            return CreateSuccessResponse("Compatible ok");
        });

        var provider = CreateProvider(new HttpClient(handler), options =>
        {
            options.ProviderName = "deepseek";
            options.ApiKey = "bearer-key";
            options.BaseUrl = "https://example.com/v1";
            options.RelativePath = "chat/completions";
        });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "deepseek-chat",
            Messages = [ChatMessage.User("Hello")]
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Bearer bearer-key", authorizationHeader);
        Assert.Equal("https://example.com/v1/chat/completions", capturedUrl);
        Assert.Contains("\"model\":\"deepseek-chat\"", body);
    }

    [Fact]
    public async Task CompleteAsync_WithApiKeyHeaderAuth_ShouldSetCustomHeader()
    {
        string? customHeader = null;

        var handler = new FakeHttpMessageHandler((request, ct) =>
        {
            customHeader = request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null;
            return Task.FromResult(CreateSuccessResponse("Compatible ok"));
        });

        var provider = CreateProvider(new HttpClient(handler), options =>
        {
            options.ProviderName = "custom-gateway";
            options.ApiKey = "header-key";
            options.BaseUrl = "https://gateway.example.com/v1/";
            options.AuthMode = OpenAICompatibleAuthMode.ApiKeyHeader;
            options.ApiKeyHeaderName = "x-api-key";
        });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "model-a",
            Messages = [ChatMessage.User("Hello")]
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("header-key", customHeader);
    }

    [Fact]
    public async Task CompleteAsync_WithToolCallResponse_ShouldMapCorrectly()
    {
        var handler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                tool_calls = new[]
                                {
                                    new { id = "call_123", type = "function", function = new { name = "lookup_weather", arguments = "{\"city\":\"Shanghai\"}" } }
                                }
                            },
                            finish_reason = "tool_calls"
                        }
                    },
                    usage = new { prompt_tokens = 11, completion_tokens = 7, total_tokens = 18 }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        });

        var provider = CreateProvider(new HttpClient(handler), options =>
        {
            options.ProviderName = "deepseek";
            options.ApiKey = "test-key";
            options.BaseUrl = "https://example.com/v1/";
        });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "deepseek-chat",
            Messages = [ChatMessage.User("What's the weather?")]
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("tool_calls", result.FinishReason);
        Assert.NotNull(result.Message?.ToolCalls);
        Assert.Single(result.Message.ToolCalls);
        Assert.Equal("call_123", result.Message.ToolCalls[0].Id);
        Assert.Equal("lookup_weather", result.Message.ToolCalls[0].Name);
        Assert.Equal("{\"city\":\"Shanghai\"}", result.Message.ToolCalls[0].Arguments);
        Assert.Equal(18, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task CompleteAsync_WithProviderOptions_ShouldSerializeThinkingAndNullAssistantToolContent()
    {
        string? body = null;
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return CreateSuccessResponse("ok");
        });

        var provider = CreateProvider(new HttpClient(handler), options =>
        {
            options.ProviderName = "deepseek";
            options.ApiKey = "test-key";
            options.BaseUrl = "https://example.com/";
            options.RelativePath = "chat/completions";
        });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "deepseek-v4-flash",
            Messages =
            [
                ChatMessage.System("You are helpful."),
                ChatMessage.User("run pwd"),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    ToolCalls =
                    [
                        new ToolCall
                        {
                            Id = "call_123",
                            Name = "run_local_command",
                            Arguments = "{\"command\":\"pwd\"}"
                        }
                    ]
                },
                new ChatMessage
                {
                    Role = ChatRole.Tool,
                    ToolCallId = "call_123",
                    TextContent = "Command: pwd\nShell: bash\nExit code: 0\nStdout:\n/tmp"
                }
            ],
            Options = new ChatOptions
            {
                ProviderOptions = new OpenAICompatibleProviderRequestOptions
                {
                    Thinking = new OpenAICompatibleThinkingOptions { Type = "disabled" }
                }
            }
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(body);

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());

        var assistant = root.GetProperty("messages")[2];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.True(assistant.TryGetProperty("content", out var assistantContent));
        Assert.Equal(JsonValueKind.Null, assistantContent.ValueKind);
        Assert.Equal("call_123", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());

        var tool = root.GetProperty("messages")[3];
        Assert.Equal("tool", tool.GetProperty("role").GetString());
        Assert.Equal("call_123", tool.GetProperty("tool_call_id").GetString());
        Assert.Equal("Command: pwd\nShell: bash\nExit code: 0\nStdout:\n/tmp", tool.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_WithInvalidResponse_ShouldReturnError()
    {
        var handler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var provider = CreateProvider(new HttpClient(handler), options =>
        {
            options.ProviderName = "deepseek";
            options.ApiKey = "test-key";
            options.BaseUrl = "https://example.com/v1/";
        });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "deepseek-chat",
            Messages = [ChatMessage.User("test")]
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid response", result.ErrorMessage);
    }

    [Fact]
    public async Task StreamAsync_WithTextAndToolCallDeltas_ShouldEmitUpdates()
    {
        var handler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta1 = new
            {
                id = "test-1",
                choices = new[]
                {
                    new
                    {
                        delta = new { content = "Hello" }
                    }
                }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta1, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");

            var delta2 = new
            {
                id = "test-1",
                choices = new[]
                {
                    new
                    {
                        delta = new
                        {
                            tool_calls = new[]
                            {
                                new { index = 0, id = "call_123", function = new { name = "get_weather", arguments = "" } }
                            }
                        }
                    }
                }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");

            var delta3 = new
            {
                id = "test-1",
                choices = new[]
                {
                    new
                    {
                        delta = new
                        {
                            tool_calls = new[]
                            {
                                new { index = 0, function = new { arguments = "{\"city\":\"Shanghai\"}" } }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                },
                usage = new { prompt_tokens = 2, completion_tokens = 3, total_tokens = 5 }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta3, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var provider = CreateProvider(new HttpClient(handler), options =>
        {
            options.ProviderName = "deepseek";
            options.ApiKey = "test-key";
            options.BaseUrl = "https://example.com/v1/";
        });

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(new ChatRequest
                       {
                           ModelId = "deepseek-chat",
                           Messages = [ChatMessage.User("test")]
                       }))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, u => u is TextDeltaUpdate text && text.Delta == "Hello");

        var toolUpdates = updates.OfType<ToolCallDeltaUpdate>().ToList();
        Assert.Equal(2, toolUpdates.Count);
        Assert.Equal("call_123", toolUpdates[0].ToolCallId);
        Assert.Equal("get_weather", toolUpdates[0].NameDelta);
        Assert.Equal("{\"city\":\"Shanghai\"}", toolUpdates[1].ArgumentsDelta);
        Assert.Contains(updates, u => u is CompletedUpdate completed && completed.FinishReason == "tool_calls");
        Assert.Contains(updates, u => u is UsageUpdate usage && usage.Usage.TotalTokens == 5);
    }

    [Fact]
    public async Task RetryHandler_With500Then200_ShouldRetryAndSucceed()
    {
        var requestCount = 0;
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(CreateSuccessResponse("Success"));
        });

        var options = new OpenAICompatibleOptions
        {
            ProviderName = "deepseek",
            ApiKey = "test-key",
            BaseUrl = "https://example.com/v1/",
            MaxRetryCount = 3,
            InitialRetryDelay = TimeSpan.FromMilliseconds(10)
        };

        var retryHandler = new OpenAICompatibleRetryHttpMessageHandler(options)
        {
            InnerHandler = fakeHandler
        };

        var provider = new OpenAICompatibleChatModelProvider(new HttpClient(retryHandler), options);

        var response = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "deepseek-chat",
            Messages = [ChatMessage.User("test")]
        });

        Assert.True(response.IsSuccess);
        Assert.Equal(2, requestCount);
        Assert.Equal("Success", response.Message?.TextContent);
    }

    private static OpenAICompatibleChatModelProvider CreateProvider(HttpClient httpClient, Action<OpenAICompatibleOptions> configure)
    {
        var options = new OpenAICompatibleOptions();
        configure(options);
        return new OpenAICompatibleChatModelProvider(httpClient, options);
    }

    private static HttpResponseMessage CreateSuccessResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json")
        };
}
