using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Core;

public abstract class OpenAICompatibleProviderBase : IChatModelProvider
{
    public abstract string ProviderName { get; }

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger? _logger;

    protected OpenAICompatibleProviderBase(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting request {RequestId} to model {ModelId} via {ProviderName}", requestId, request.ModelId, ProviderName);

        try
        {
            var providerRequest = CreateProviderRequest(request, stream: false);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var response = await _httpClient.PostAsync(BuildRelativeUrl(request.ModelId), new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var providerResponse = JsonSerializer.Deserialize<OpenAICompatibleChatCompletionResponse>(responseJson, _jsonOptions);
            _logger?.LogInformation("Request {RequestId} completed successfully", requestId);
            return MapFromResponse(providerResponse, GetInvalidResponseMessage());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Request {RequestId} failed", requestId);
            return new ChatResponse { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async IAsyncEnumerable<StreamingChatUpdate> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting streaming request {RequestId} to model {ModelId} via {ProviderName}", requestId, request.ModelId, ProviderName);

        HttpResponseMessage? response = null;
        Exception? initialException = null;

        try
        {
            var providerRequest = CreateProviderRequest(request, stream: true);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildRelativeUrl(request.ModelId))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Streaming request {RequestId} failed during initialization", requestId);
            initialException = ex;
        }

        if (initialException != null)
        {
            yield return new ErrorUpdate(initialException.Message);
            yield break;
        }

        if (response == null)
            yield break;

        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("data: "))
                line = line["data: ".Length..];
            if (line == "[DONE]")
                break;

            OpenAICompatibleChatCompletionStreamResponse? streamingResponse;
            try
            {
                streamingResponse = JsonSerializer.Deserialize<OpenAICompatibleChatCompletionStreamResponse>(line, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize streaming line");
                continue;
            }

            if (streamingResponse?.Choices != null && streamingResponse.Choices.Count > 0)
            {
                var choice = streamingResponse.Choices[0];
                var delta = choice.Delta;

                if (!string.IsNullOrEmpty(delta?.Content))
                    yield return new TextDeltaUpdate(delta.Content);

                if (delta?.ToolCalls != null && delta.ToolCalls.Count > 0)
                {
                    foreach (var toolCall in delta.ToolCalls)
                    {
                        if (!toolCall.Index.HasValue)
                            continue;

                        var index = toolCall.Index.Value;
                        if (!toolCallAccumulators.TryGetValue(index, out var accumulator))
                        {
                            accumulator = new ToolCallAccumulator();
                            toolCallAccumulators[index] = accumulator;
                        }

                        if (!string.IsNullOrEmpty(toolCall.Id))
                            accumulator.Id = toolCall.Id;
                        if (!string.IsNullOrEmpty(toolCall.Function?.Name))
                            accumulator.Name = toolCall.Function.Name;
                        if (!string.IsNullOrEmpty(toolCall.Function?.Arguments))
                            accumulator.Arguments += toolCall.Function.Arguments;

                        yield return new ToolCallDeltaUpdate(accumulator.Id ?? string.Empty, toolCall.Function?.Name, toolCall.Function?.Arguments);
                    }
                }

                if (!string.IsNullOrEmpty(choice.FinishReason))
                    yield return new CompletedUpdate(choice.FinishReason);
            }

            if (streamingResponse?.Usage != null)
            {
                yield return new UsageUpdate(new UsageInfo
                {
                    PromptTokens = streamingResponse.Usage.PromptTokens,
                    CompletionTokens = streamingResponse.Usage.CompletionTokens,
                    TotalTokens = streamingResponse.Usage.TotalTokens
                });
            }
        }

        _logger?.LogInformation("Streaming request {RequestId} completed", requestId);
    }

    protected OpenAICompatibleChatCompletionRequest CreateBaseRequest(ChatRequest request, bool stream, bool includeModel)
    {
        var providerRequest = new OpenAICompatibleChatCompletionRequest
        {
            Stream = stream,
            Model = includeModel ? request.ModelId : null,
            Messages = request.Messages.Select(MapToMessage).ToList(),
            Temperature = request.Options?.Temperature,
            TopP = request.Options?.TopP,
            MaxTokens = request.Options?.MaxTokens,
            ReasoningEffort = NormalizeThinkingIntensity(request.Options?.ThinkingIntensity),
            Stop = request.Options?.StopSequences
        };

        if (request.Options?.Tools != null && request.Options.Tools.Count > 0)
        {
            providerRequest.Tools = request.Options.Tools.Select(t => new OpenAICompatibleToolDefinition
            {
                Type = "function",
                Function = new OpenAICompatibleFunctionDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.ParametersSchema
                }
            }).ToList();
        }

        return providerRequest;
    }

    protected virtual OpenAICompatibleMessage MapToMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };

        var providerMessage = new OpenAICompatibleMessage
        {
            Role = role,
            Content = BuildTextContent(message),
            ToolCallId = message.ToolCallId
        };

        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
        {
            providerMessage.ToolCalls = message.ToolCalls.Select(tc => new OpenAICompatibleToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new OpenAICompatibleFunctionCall
                {
                    Name = tc.Name,
                    Arguments = tc.Arguments
                }
            }).ToList();
        }

        return providerMessage;
    }

    protected static string? BuildTextContent(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.TextContent))
        {
            return message.TextContent;
        }

        if (message.ContentParts == null || message.ContentParts.Count == 0)
        {
            return null;
        }

        return string.Join("\n", message.ContentParts.Select(MapContentPartToText));
    }

    protected static string MapContentPartToText(ContentPart part)
        => part switch
        {
            TextPart text => text.Text,
            ImageUrlPart image => $"[image: {image.Url}]",
            BinaryPart binary => $"[binary: {binary.MediaType}, {binary.Data.Length} bytes]",
            _ => "[unsupported content part]"
        };

    public static string? NormalizeThinkingIntensity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "minimal" or "low" or "medium" or "high" => normalized,
            _ => value.Trim()
        };
    }

    protected virtual ChatResponse MapFromResponse(OpenAICompatibleChatCompletionResponse? response, string invalidResponseMessage)
    {
        if (response?.Choices == null || response.Choices.Count == 0)
        {
            return new ChatResponse { IsSuccess = false, ErrorMessage = invalidResponseMessage };
        }

        var choice = response.Choices[0];
        var message = choice.Message;
        IReadOnlyList<ToolCall>? toolCalls = null;

        if (message?.ToolCalls != null && message.ToolCalls.Count > 0)
        {
            toolCalls = message.ToolCalls.Select(tc => new ToolCall
            {
                Id = tc.Id,
                Name = tc.Function?.Name ?? string.Empty,
                Arguments = tc.Function?.Arguments ?? string.Empty
            }).ToList();
        }

        return new ChatResponse
        {
            IsSuccess = true,
            Message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                TextContent = message?.Content,
                ToolCalls = toolCalls
            },
            FinishReason = choice.FinishReason,
            Usage = response.Usage == null ? null : new UsageInfo
            {
                PromptTokens = response.Usage.PromptTokens,
                CompletionTokens = response.Usage.CompletionTokens,
                TotalTokens = response.Usage.TotalTokens
            }
        };
    }

    protected abstract object CreateProviderRequest(ChatRequest request, bool stream);
    protected abstract string BuildRelativeUrl(string modelOrDeployment);
    protected abstract string GetInvalidResponseMessage();
}

public class OpenAICompatibleChatCompletionRequest
{
    public string? Model { get; set; }
    public List<OpenAICompatibleMessage> Messages { get; set; } = [];
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public string? ReasoningEffort { get; set; }
    public IReadOnlyList<string>? Stop { get; set; }
    public List<OpenAICompatibleToolDefinition>? Tools { get; set; }
    public bool? Stream { get; set; }
}

public class OpenAICompatibleMessage
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ToolCallId { get; set; }
    public List<OpenAICompatibleToolCall>? ToolCalls { get; set; }
}

public class OpenAICompatibleToolDefinition
{
    public string Type { get; set; } = string.Empty;
    public OpenAICompatibleFunctionDefinition Function { get; set; } = null!;
}

public class OpenAICompatibleFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Parameters { get; set; }
}

public class OpenAICompatibleToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public OpenAICompatibleFunctionCall Function { get; set; } = null!;
    public int? Index { get; set; }
}

public class OpenAICompatibleFunctionCall
{
    public string? Name { get; set; }
    public string? Arguments { get; set; }
}

public class OpenAICompatibleChatCompletionResponse
{
    public List<OpenAICompatibleChoice>? Choices { get; set; }
    public OpenAICompatibleUsage? Usage { get; set; }
}

public class OpenAICompatibleChoice
{
    public OpenAICompatibleMessage? Message { get; set; }
    public string? FinishReason { get; set; }
}

public class OpenAICompatibleUsage
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

public class OpenAICompatibleChatCompletionStreamResponse
{
    public List<OpenAICompatibleStreamChoice>? Choices { get; set; }
    public OpenAICompatibleUsage? Usage { get; set; }
}

public class OpenAICompatibleStreamChoice
{
    public OpenAICompatibleDelta? Delta { get; set; }
    public string? FinishReason { get; set; }
}

public class OpenAICompatibleDelta
{
    public string? Content { get; set; }
    public List<OpenAICompatibleToolCall>? ToolCalls { get; set; }
}

internal class ToolCallAccumulator
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string Arguments { get; set; } = string.Empty;
}
