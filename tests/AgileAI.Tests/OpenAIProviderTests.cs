using System.Net;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Providers.OpenAI;

namespace AgileAI.Tests;

public class OpenAIProviderTests
{
    [Fact]
    public void OpenAIChatModelProvider_ProviderName_ShouldBeOpenai()
    {
        var httpClient = new HttpClient();
        var options = new OpenAIOptions { ApiKey = "test-key" };
        var provider = new OpenAIChatModelProvider(httpClient, options);

        Assert.Equal("openai", provider.ProviderName);
    }

    [Fact]
    public async Task CompleteAsync_WithInvalidApiKey_ShouldReturnError()
    {
        var httpClient = new HttpClient();
        var options = new OpenAIOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
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
        var options = new OpenAIOptions { ApiKey = "invalid-key", BaseUrl = "https://example.invalid/" };
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var request = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
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
    public async Task RetryHttpMessageHandler_With500Then200_ShouldSucceedOnRetry()
    {
        var requestCount = 0;
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Success"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key", MaxRetryCount = 3, InitialRetryDelay = TimeSpan.FromMilliseconds(10) };
        var retryHandler = new RetryHttpMessageHandler(options)
        {
            InnerHandler = fakeHandler
        };
        var httpClient = new HttpClient(retryHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var response = await provider.CompleteAsync(chatRequest);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, requestCount);
        Assert.Equal("Success", response.Message?.TextContent);
    }

    [Fact]
    public async Task StreamAsync_WithToolCallDeltas_ShouldAccumulateToolCallInfo()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
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
                                new { index = 0, function = new { arguments = "{\"location\":\"" } }
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
                                new { index = 0, function = new { arguments = "San Francisco\"}" } }
                            }
                        }
                    }
                }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta3, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            
            var delta4 = new
            {
                id = "test-1",
                choices = new[] { new { delta = new { }, finish_reason = "tool_calls" } }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta4, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("What's the weather?")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var toolCallUpdates = updates.OfType<ToolCallDeltaUpdate>().ToList();
        Assert.Equal(3, toolCallUpdates.Count);
        
        Assert.Equal("call_123", toolCallUpdates[0].ToolCallId);
        Assert.Equal("get_weather", toolCallUpdates[0].NameDelta);
        Assert.Equal("", toolCallUpdates[0].ArgumentsDelta);
        
        Assert.Equal("call_123", toolCallUpdates[1].ToolCallId);
        Assert.Null(toolCallUpdates[1].NameDelta);
        Assert.Equal("{\"location\":\"", toolCallUpdates[1].ArgumentsDelta);
        
        Assert.Equal("call_123", toolCallUpdates[2].ToolCallId);
        Assert.Null(toolCallUpdates[2].NameDelta);
        Assert.Equal("San Francisco\"}", toolCallUpdates[2].ArgumentsDelta);
    }

    [Fact]
    public async Task RetryHttpMessageHandler_WithMaxRetriesReached_ShouldReturnLastFailure()
    {
        var requestCount = 0;
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        var options = new OpenAIOptions { ApiKey = "test-key", MaxRetryCount = 2, InitialRetryDelay = TimeSpan.FromMilliseconds(10) };
        var retryHandler = new RetryHttpMessageHandler(options)
        {
            InnerHandler = fakeHandler
        };
        var httpClient = new HttpClient(retryHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var response = await provider.CompleteAsync(chatRequest);

        Assert.False(response.IsSuccess);
        Assert.Equal(3, requestCount);
    }

    [Fact]
    public async Task RetryHttpMessageHandler_With400BadRequest_ShouldNotRetry()
    {
        var requestCount = 0;
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var options = new OpenAIOptions { ApiKey = "test-key", MaxRetryCount = 3, InitialRetryDelay = TimeSpan.FromMilliseconds(10) };
        var retryHandler = new RetryHttpMessageHandler(options)
        {
            InnerHandler = fakeHandler
        };
        var httpClient = new HttpClient(retryHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var response = await provider.CompleteAsync(chatRequest);

        Assert.False(response.IsSuccess);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task StreamAsync_WithInvalidJsonLine_ShouldSkipAndContinue()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();
            
            var validDelta = new
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
            streamContent.AppendLine("data: {invalid json}");
            streamContent.AppendLine("data: ");
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(validDelta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var textUpdates = updates.OfType<TextDeltaUpdate>().ToList();
        Assert.Single(textUpdates);
        Assert.Equal("Hello", textUpdates[0].Delta);
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
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "ok"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var provider = new OpenAIChatModelProvider(new HttpClient(fakeHandler), new OpenAIOptions { ApiKey = "test-key" });

        await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "gpt-4o",
            Messages =
            [
                ChatMessage.User(new TextPart("Describe this image"), new ImageUrlPart("https://example.com/dog.png"))
            ]
        });

        Assert.Contains("Describe this image", capturedRequestContent);
        Assert.Contains("[image: https://example.com/dog.png]", capturedRequestContent);
    }

    [Fact]
    public async Task StreamAsync_WithToolDeltaOnlyArguments_ShouldYieldArguments()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
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
                        delta = new
                        {
                            tool_calls = new[]
                            {
                                new { index = 0, id = "call_456", function = new { name = "test_tool", arguments = "" } }
                            }
                        }
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
                                new { index = 0, function = new { arguments = "{\"key\":\"value\"}" } }
                            }
                        }
                    }
                }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var toolUpdates = updates.OfType<ToolCallDeltaUpdate>().ToList();
        Assert.Equal(2, toolUpdates.Count);
        Assert.Equal("{\"key\":\"value\"}", toolUpdates[1].ArgumentsDelta);
    }

    [Fact]
    public async Task StreamAsync_WithToolDeltaOnlyName_ShouldYieldName()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();
            
            var delta = new
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
                                new { index = 0, id = "call_789", function = new { name = "only_name_tool", arguments = "" } }
                            }
                        }
                    }
                }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var toolUpdates = updates.OfType<ToolCallDeltaUpdate>().ToList();
        Assert.Single(toolUpdates);
        Assert.Equal("only_name_tool", toolUpdates[0].NameDelta);
    }

    [Fact]
    public async Task StreamAsync_WithToolDeltaMissingId_ShouldUseEmptyId()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();
            
            var delta = new
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
                                new { index = 0, function = new { name = "no_id_tool", arguments = "{}" } }
                            }
                        }
                    }
                }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var toolUpdates = updates.OfType<ToolCallDeltaUpdate>().ToList();
        Assert.Single(toolUpdates);
        Assert.Equal(string.Empty, toolUpdates[0].ToolCallId);
    }

    [Fact]
    public async Task CompleteAsync_WithSystemMessage_ShouldMapCorrectly()
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
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Response"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages =
            [
                new ChatMessage { Role = ChatRole.System, TextContent = "You are a helpful assistant" },
                ChatMessage.User("Hello")
            ]
        };

        await provider.CompleteAsync(chatRequest);

        Assert.Contains("\"role\":\"system\"", capturedRequestContent);
        Assert.Contains("\"You are a helpful assistant\"", capturedRequestContent);
    }

    [Fact]
    public async Task CompleteAsync_WithAssistantMessage_ShouldMapCorrectly()
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
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Response"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages =
            [
                ChatMessage.User("Hello"),
                new ChatMessage { Role = ChatRole.Assistant, TextContent = "Hi there!" }
            ]
        };

        await provider.CompleteAsync(chatRequest);

        Assert.Contains("\"role\":\"assistant\"", capturedRequestContent);
        Assert.Contains("\"Hi there!\"", capturedRequestContent);
    }

    [Fact]
    public async Task CompleteAsync_WithToolMessage_ShouldMapCorrectly()
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
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Response"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages =
            [
                ChatMessage.User("Hello"),
                new ChatMessage { Role = ChatRole.Tool, ToolCallId = "call_123", TextContent = "{\"result\":\"ok\"}" }
            ]
        };

        await provider.CompleteAsync(chatRequest);

        Assert.Contains("\"role\":\"tool\"", capturedRequestContent);
        Assert.Contains("\"tool_call_id\":\"call_123\"", capturedRequestContent);
    }

    [Fact]
    public async Task CompleteAsync_WithMessageToolCalls_ShouldMapCorrectly()
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
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Response"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages =
            [
                ChatMessage.User("Hello"),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    ToolCalls = new List<ToolCall>
                    {
                        new() { Id = "call_123", Name = "get_weather", Arguments = "{\"location\":\"SF\"}" }
                    }
                }
            ]
        };

        await provider.CompleteAsync(chatRequest);

        Assert.Contains("\"tool_calls\"", capturedRequestContent);
        Assert.Contains("\"id\":\"call_123\"", capturedRequestContent);
        Assert.Contains("\"name\":\"get_weather\"", capturedRequestContent);
    }

    [Fact]
    public async Task CompleteAsync_WithTools_ShouldMapCorrectly()
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
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Response"
                        },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return response;
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("Hello")],
            Options = new ChatOptions
            {
                Tools = new List<ToolDefinition>
                {
                    new() { Name = "get_weather", Description = "Get weather", ParametersSchema = new { type = "object" } }
                }
            }
        };

        await provider.CompleteAsync(chatRequest);

        Assert.Contains("\"tools\"", capturedRequestContent);
        Assert.Contains("\"name\":\"get_weather\"", capturedRequestContent);
    }

    [Fact]
    public async Task CompleteAsync_WithToolCallResponse_ShouldMapCorrectly()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
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
                                new { id = "call_123", type = "function", function = new { name = "get_weather", arguments = "{\"location\":\"SF\"}" } }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("What's the weather?")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal("tool_calls", result.FinishReason);
        Assert.NotNull(result.Message?.ToolCalls);
        Assert.Single(result.Message.ToolCalls);
        Assert.Equal("call_123", result.Message.ToolCalls[0].Id);
        Assert.Equal("get_weather", result.Message.ToolCalls[0].Name);
        Assert.Equal("{\"location\":\"SF\"}", result.Message.ToolCalls[0].Arguments);
    }

    [Fact]
    public async Task CompleteAsync_WithNullChoices_ShouldReturnError()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new { }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid response", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_WithEmptyChoices_ShouldReturnError()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new { choices = new object[] { } }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid response", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_WithNullMessage_ShouldHandleGracefully()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[] { new { finish_reason = "stop" } }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Message?.TextContent);
    }

    [Fact]
    public async Task CompleteAsync_WithUsage_ShouldMapCorrectly()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content = "Response" },
                        finish_reason = "stop"
                    }
                },
                usage = new { prompt_tokens = 10, completion_tokens = 20, total_tokens = 30 }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Usage);
        Assert.Equal(10, result.Usage.PromptTokens);
        Assert.Equal(20, result.Usage.CompletionTokens);
        Assert.Equal(30, result.Usage.TotalTokens);
    }

    [Fact]
    public async Task CompleteAsync_WithoutUsage_ShouldHandleGracefully()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content = "Response" },
                        finish_reason = "stop"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var result = await provider.CompleteAsync(chatRequest);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task StreamAsync_WithOnlyCompleted_ShouldYieldCompleted()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta = new
            {
                id = "test-1",
                choices = new[] { new { delta = new { }, finish_reason = "stop" } }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var completedUpdates = updates.OfType<CompletedUpdate>().ToList();
        Assert.Single(completedUpdates);
        Assert.Equal("stop", completedUpdates[0].FinishReason);
    }

    [Fact]
    public async Task StreamAsync_WithOnlyUsage_ShouldYieldUsage()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta = new
            {
                id = "test-1",
                usage = new { prompt_tokens = 5, completion_tokens = 15, total_tokens = 20 }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var usageUpdates = updates.OfType<UsageUpdate>().ToList();
        Assert.Single(usageUpdates);
        Assert.Equal(5, usageUpdates[0].Usage.PromptTokens);
        Assert.Equal(15, usageUpdates[0].Usage.CompletionTokens);
        Assert.Equal(20, usageUpdates[0].Usage.TotalTokens);
    }

    [Fact]
    public async Task StreamAsync_WithEmptyChoices_ShouldHandleGracefully()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta1 = new
            {
                id = "test-1",
                choices = new object[] { }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta1, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");

            var delta2 = new
            {
                id = "test-1",
                choices = new[] { new { delta = new { content = "Hello" } } }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var textUpdates = updates.OfType<TextDeltaUpdate>().ToList();
        Assert.Single(textUpdates);
        Assert.Equal("Hello", textUpdates[0].Delta);
    }

    [Fact]
    public async Task StreamAsync_WithNullChoices_ShouldHandleGracefully()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta1 = new { id = "test-1" };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta1, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");

            var delta2 = new
            {
                id = "test-1",
                choices = new[] { new { delta = new { content = "Hello" } } }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var textUpdates = updates.OfType<TextDeltaUpdate>().ToList();
        Assert.Single(textUpdates);
        Assert.Equal("Hello", textUpdates[0].Delta);
    }

    [Fact]
    public async Task StreamAsync_WithNullDelta_ShouldHandleGracefully()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta1 = new
            {
                id = "test-1",
                choices = new[] { new { finish_reason = "stop" } }
            };
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta1, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var options = new OpenAIOptions { ApiKey = "test-key" };
        var httpClient = new HttpClient(fakeHandler);
        var provider = new OpenAIChatModelProvider(httpClient, options);

        var chatRequest = new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        };

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(chatRequest))
        {
            updates.Add(update);
        }

        var completedUpdates = updates.OfType<CompletedUpdate>().ToList();
        Assert.Single(completedUpdates);
    }

    [Fact]
    public async Task CompleteAsync_WithUnknownFinishReason_ShouldPreserveValue()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content = "Response" },
                        finish_reason = "length"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var provider = new OpenAIChatModelProvider(new HttpClient(fakeHandler), new OpenAIOptions { ApiKey = "test-key" });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("length", result.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_WithToolCallMissingFunction_ShouldDefaultToEmptyStrings()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(JsonSerializer.Serialize(new
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
                                new { id = "call_missing", type = "function" }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });

        var provider = new OpenAIChatModelProvider(new HttpClient(fakeHandler), new OpenAIOptions { ApiKey = "test-key" });

        var result = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Message?.ToolCalls);
        Assert.Single(result.Message.ToolCalls);
        Assert.Equal("call_missing", result.Message.ToolCalls[0].Id);
        Assert.Equal(string.Empty, result.Message.ToolCalls[0].Name);
        Assert.Equal(string.Empty, result.Message.ToolCalls[0].Arguments);
    }

    [Fact]
    public async Task StreamAsync_WithCombinedChunk_ShouldYieldTextToolCompletedAndUsageInOrder()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var delta = new
            {
                id = "test-1",
                choices = new[]
                {
                    new
                    {
                        delta = new
                        {
                            content = "Hello",
                            tool_calls = new[]
                            {
                                new { index = 0, id = "call_999", function = new { name = "demo_tool", arguments = "{\"a\":1}" } }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                },
                usage = new { prompt_tokens = 1, completion_tokens = 2, total_tokens = 3 }
            };

            streamContent.AppendLine($"data: {JsonSerializer.Serialize(delta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var provider = new OpenAIChatModelProvider(new HttpClient(fakeHandler), new OpenAIOptions { ApiKey = "test-key" });

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        }))
        {
            updates.Add(update);
        }

        Assert.Collection(updates,
            update => Assert.Equal("Hello", Assert.IsType<TextDeltaUpdate>(update).Delta),
            update =>
            {
                var tool = Assert.IsType<ToolCallDeltaUpdate>(update);
                Assert.Equal("call_999", tool.ToolCallId);
                Assert.Equal("demo_tool", tool.NameDelta);
                Assert.Equal("{\"a\":1}", tool.ArgumentsDelta);
            },
            update => Assert.Equal("tool_calls", Assert.IsType<CompletedUpdate>(update).FinishReason),
            update =>
            {
                var usage = Assert.IsType<UsageUpdate>(update).Usage;
                Assert.Equal(1, usage.PromptTokens);
                Assert.Equal(2, usage.CompletionTokens);
                Assert.Equal(3, usage.TotalTokens);
            });
    }

    [Fact]
    public async Task StreamAsync_WithUsageThenCompletedAcrossChunks_ShouldYieldBoth()
    {
        var fakeHandler = new FakeHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var streamContent = new StringBuilder();

            var usageOnly = new
            {
                id = "test-1",
                usage = new { prompt_tokens = 5, completion_tokens = 7, total_tokens = 12 }
            };
            var completedOnly = new
            {
                id = "test-1",
                choices = new[] { new { delta = new { }, finish_reason = "stop" } }
            };

            streamContent.AppendLine($"data: {JsonSerializer.Serialize(usageOnly, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine($"data: {JsonSerializer.Serialize(completedOnly, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })}");
            streamContent.AppendLine("data: [DONE]");

            response.Content = new StringContent(streamContent.ToString(), Encoding.UTF8, "text/event-stream");
            return Task.FromResult(response);
        });

        var provider = new OpenAIChatModelProvider(new HttpClient(fakeHandler), new OpenAIOptions { ApiKey = "test-key" });

        var updates = new List<StreamingChatUpdate>();
        await foreach (var update in provider.StreamAsync(new ChatRequest
        {
            ModelId = "gpt-3.5-turbo",
            Messages = [ChatMessage.User("test")]
        }))
        {
            updates.Add(update);
        }

        Assert.Collection(updates,
            update =>
            {
                var usage = Assert.IsType<UsageUpdate>(update).Usage;
                Assert.Equal(12, usage.TotalTokens);
            },
            update => Assert.Equal("stop", Assert.IsType<CompletedUpdate>(update).FinishReason));
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
