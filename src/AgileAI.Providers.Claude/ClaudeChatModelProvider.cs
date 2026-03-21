using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Providers.Claude;

public class ClaudeChatModelProvider : IChatModelProvider
{
    public string ProviderName => "claude";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<ClaudeChatModelProvider>? _logger;

    public ClaudeChatModelProvider(
        HttpClient httpClient,
        ClaudeOptions options,
        ILogger<ClaudeChatModelProvider>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        httpClient.BaseAddress = new Uri(options.BaseUrl ?? "https://api.anthropic.com/v1/");
        httpClient.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", options.Version);
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting Claude request {RequestId} to model {ModelId}", requestId, request.ModelId);

        try
        {
            var providerRequest = CreateProviderRequest(request, stream: false);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var response = await _httpClient.PostAsync("messages", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var providerResponse = JsonSerializer.Deserialize<ClaudeMessagesResponse>(responseJson, _jsonOptions);
            _logger?.LogInformation("Claude request {RequestId} completed successfully", requestId);
            return MapFromResponse(providerResponse);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Claude request {RequestId} failed", requestId);
            return new ChatResponse { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async IAsyncEnumerable<StreamingChatUpdate> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting streaming Claude request {RequestId} to model {ModelId}", requestId, request.ModelId);

        HttpResponseMessage? response = null;
        Exception? initialException = null;

        try
        {
            var providerRequest = CreateProviderRequest(request, stream: true);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Streaming Claude request {RequestId} failed during initialization", requestId);
            initialException = ex;
        }

        if (initialException != null)
        {
            yield return new ErrorUpdate(initialException.Message);
            yield break;
        }

        if (response == null)
            yield break;

        var toolCallAccumulators = new Dictionary<string, ToolCallAccumulator>();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("data: "))
                line = line["data: ".Length..];

            ClaudeMessagesStreamEvent? streamEvent;
            try
            {
                streamEvent = JsonSerializer.Deserialize<ClaudeMessagesStreamEvent>(line, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize streaming line");
                continue;
            }

            if (streamEvent?.Type == "content_block_delta")
            {
                if (!string.IsNullOrEmpty(streamEvent.Delta?.Text))
                    yield return new TextDeltaUpdate(streamEvent.Delta.Text);
                else if (streamEvent.Delta?.PartialJson != null)
                {
                    if (streamEvent.Index.HasValue && toolCallAccumulators.TryGetValue(streamEvent.Index.Value.ToString(), out var accumulator))
                    {
                        accumulator.Arguments += streamEvent.Delta.PartialJson;
                        yield return new ToolCallDeltaUpdate(accumulator.Id ?? string.Empty, accumulator.Name, streamEvent.Delta.PartialJson);
                    }
                }
            }
            else if (streamEvent?.Type == "content_block_start")
            {
                if (streamEvent.ContentBlock?.Type == "tool_use")
                {
                    var index = streamEvent.Index ?? 0;
                    var accumulator = new ToolCallAccumulator
                    {
                        Id = streamEvent.ContentBlock.Id,
                        Name = streamEvent.ContentBlock.Name
                    };
                    toolCallAccumulators[index.ToString()] = accumulator;
                }
            }
            else if (streamEvent?.Type == "message_stop")
            {
                yield return new CompletedUpdate("stop");
            }
            else if (streamEvent?.Type == "message_delta" && streamEvent.Usage != null)
            {
                yield return new UsageUpdate(new UsageInfo
                {
                    PromptTokens = streamEvent.Usage.InputTokens,
                    CompletionTokens = streamEvent.Usage.OutputTokens,
                    TotalTokens = streamEvent.Usage.InputTokens + streamEvent.Usage.OutputTokens
                });
            }
        }

        _logger?.LogInformation("Streaming Claude request {RequestId} completed", requestId);
    }

    private ClaudeMessagesRequest CreateProviderRequest(ChatRequest request, bool stream)
    {
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role == ChatRole.System);
        var messages = request.Messages.Where(m => m.Role != ChatRole.System).Select(MapToMessage).ToList();

        var providerRequest = new ClaudeMessagesRequest
        {
            Model = request.ModelId.StartsWith("claude:") ? request.ModelId.Substring("claude:".Length) : request.ModelId,
            Stream = stream,
            Messages = messages,
            MaxTokens = request.Options?.MaxTokens ?? 1024,
            Temperature = request.Options?.Temperature,
            TopP = request.Options?.TopP,
            StopSequences = request.Options?.StopSequences
        };

        if (systemMessage != null && !string.IsNullOrEmpty(systemMessage.TextContent))
        {
            providerRequest.System = systemMessage.TextContent;
        }

        if (request.Options?.Tools != null && request.Options.Tools.Count > 0)
        {
            providerRequest.Tools = request.Options.Tools.Select(t => new ClaudeToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.ParametersSchema
            }).ToList();
        }

        return providerRequest;
    }

    private ClaudeMessage MapToMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "user",
            _ => "user"
        };

        var content = new List<ClaudeContentBlock>();

        if (!string.IsNullOrEmpty(message.TextContent))
        {
            if (message.Role == ChatRole.Tool)
            {
                content.Add(new ClaudeContentBlock
                {
                    Type = "tool_result",
                    ToolUseId = message.ToolCallId,
                    Content = message.TextContent
                });
            }
            else
            {
                content.Add(new ClaudeContentBlock
                {
                    Type = "text",
                    Text = message.TextContent
                });
            }
        }
        else if (message.ContentParts != null && message.ContentParts.Count > 0)
        {
            foreach (var part in message.ContentParts)
            {
                if (part is TextPart textPart)
                {
                    content.Add(new ClaudeContentBlock
                    {
                        Type = "text",
                        Text = textPart.Text
                    });
                }
                else
                {
                    content.Add(new ClaudeContentBlock
                    {
                        Type = "text",
                        Text = MapUnsupportedContentPartToText(part)
                    });
                }
            }
        }

        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
        {
            foreach (var toolCall in message.ToolCalls)
            {
                content.Add(new ClaudeContentBlock
                {
                    Type = "tool_use",
                    Id = toolCall.Id,
                    Name = toolCall.Name,
                    Input = JsonSerializer.Deserialize<Dictionary<string, object>>(toolCall.Arguments)
                });
            }
        }

        return new ClaudeMessage
        {
            Role = role,
            Content = content
        };
    }

    private ChatResponse MapFromResponse(ClaudeMessagesResponse? response)
    {
        if (response == null)
        {
            return new ChatResponse { IsSuccess = false, ErrorMessage = "Invalid response from Claude" };
        }

        var outputText = string.Empty;
        IReadOnlyList<ToolCall>? toolCalls = null;
        var toolCallList = new List<ToolCall>();

        if (response.Content != null)
        {
            foreach (var block in response.Content)
            {
                if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                {
                    outputText += block.Text;
                }
                else if (block.Type == "tool_use")
                {
                    toolCallList.Add(new ToolCall
                    {
                        Id = block.Id ?? string.Empty,
                        Name = block.Name ?? string.Empty,
                        Arguments = JsonSerializer.Serialize(block.Input)
                    });
                }
            }
        }

        if (toolCallList.Count > 0)
        {
            toolCalls = toolCallList;
        }

        return new ChatResponse
        {
            IsSuccess = true,
            Message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                TextContent = outputText,
                ToolCalls = toolCalls
            },
            FinishReason = response.StopReason,
            Usage = response.Usage == null ? null : new UsageInfo
            {
                PromptTokens = response.Usage.InputTokens,
                CompletionTokens = response.Usage.OutputTokens,
                TotalTokens = response.Usage.InputTokens + response.Usage.OutputTokens
            }
        };
    }

    private static string MapUnsupportedContentPartToText(ContentPart part)
        => part switch
        {
            ImageUrlPart image => $"[image: {image.Url}]",
            BinaryPart binary => $"[binary: {binary.MediaType}, {binary.Data.Length} bytes]",
            _ => "[unsupported content part]"
        };
}

public class ClaudeMessagesRequest
{
    public string Model { get; set; } = string.Empty;
    public bool? Stream { get; set; }
    public List<ClaudeMessage> Messages { get; set; } = [];
    public string? System { get; set; }
    public int MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public List<ClaudeToolDefinition>? Tools { get; set; }
}

public class ClaudeMessage
{
    public string Role { get; set; } = string.Empty;
    public List<ClaudeContentBlock> Content { get; set; } = [];
}

public class ClaudeContentBlock
{
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, object>? Input { get; set; }
    public string? ToolUseId { get; set; }
    public string? Content { get; set; }
}

public class ClaudeToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? InputSchema { get; set; }
}

public class ClaudeMessagesResponse
{
    public List<ClaudeContentBlock>? Content { get; set; }
    public string? StopReason { get; set; }
    public ClaudeUsage? Usage { get; set; }
}

public class ClaudeUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

public class ClaudeMessagesStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public int? Index { get; set; }
    public ClaudeContentBlock? ContentBlock { get; set; }
    public ClaudeContentBlockDelta? Delta { get; set; }
    public ClaudeUsage? Usage { get; set; }
}

public class ClaudeContentBlockDelta
{
    public string? Text { get; set; }
    public string? PartialJson { get; set; }
}

internal class ToolCallAccumulator
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string Arguments { get; set; } = string.Empty;
}
