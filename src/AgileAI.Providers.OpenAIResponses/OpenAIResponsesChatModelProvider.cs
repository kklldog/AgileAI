using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using AgileAI.Core;
using Microsoft.Extensions.Logging;

namespace AgileAI.Providers.OpenAIResponses;

public class OpenAIResponsesChatModelProvider : IChatModelProvider
{
    public string ProviderName => "openai-responses";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<OpenAIResponsesChatModelProvider>? _logger;

    public OpenAIResponsesChatModelProvider(
        HttpClient httpClient,
        OpenAIResponsesOptions options,
        ILogger<OpenAIResponsesChatModelProvider>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        httpClient.BaseAddress = new Uri(options.BaseUrl ?? "https://api.openai.com/v1/");
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting Responses API request {RequestId} to model {ModelId}", requestId, request.ModelId);

        try
        {
            var providerRequest = CreateProviderRequest(request, stream: false);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var response = await _httpClient.PostAsync("responses", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var providerResponse = JsonSerializer.Deserialize<OpenAIResponsesResponse>(responseJson, _jsonOptions);
            _logger?.LogInformation("Responses API request {RequestId} completed successfully", requestId);
            return MapFromResponse(providerResponse);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Responses API request {RequestId} failed", requestId);
            return new ChatResponse { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async IAsyncEnumerable<StreamingChatUpdate> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting streaming Responses API request {RequestId} to model {ModelId}", requestId, request.ModelId);

        HttpResponseMessage? response = null;
        Exception? initialException = null;

        try
        {
            var providerRequest = CreateProviderRequest(request, stream: true);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Streaming Responses API request {RequestId} failed during initialization", requestId);
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

            OpenAIResponsesStreamEvent? streamEvent;
            try
            {
                streamEvent = JsonSerializer.Deserialize<OpenAIResponsesStreamEvent>(line, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize streaming line");
                continue;
            }

            if (streamEvent?.Type == "response.output_text.delta")
            {
                if (!string.IsNullOrEmpty(streamEvent.Delta))
                    yield return new TextDeltaUpdate(streamEvent.Delta);
            }
            else if (streamEvent?.Type == "response.output_item.done" && streamEvent.Item?.Type == "function_call")
            {
                var toolCall = streamEvent.Item;
                if (toolCall != null)
                {
                    yield return new ToolCallDeltaUpdate(toolCall.Id ?? string.Empty, toolCall.Name, toolCall.Arguments);
                }
            }
            else if (streamEvent?.Type == "response.finished")
            {
                yield return new CompletedUpdate("stop");
                if (streamEvent.Usage != null)
                {
                    yield return new UsageUpdate(new UsageInfo
                    {
                        PromptTokens = streamEvent.Usage.InputTokens,
                        CompletionTokens = streamEvent.Usage.OutputTokens,
                        TotalTokens = streamEvent.Usage.TotalTokens
                    });
                }
            }
        }

        _logger?.LogInformation("Streaming Responses API request {RequestId} completed", requestId);
    }

    private OpenAIResponsesRequest CreateProviderRequest(ChatRequest request, bool stream)
    {
        var providerRequest = new OpenAIResponsesRequest
        {
            Model = request.ModelId,
            Stream = stream,
            Input = request.Messages.Select(MapToInputItem).ToList(),
            Temperature = request.Options?.Temperature,
            TopP = request.Options?.TopP,
            MaxOutputTokens = request.Options?.MaxTokens,
            Stop = request.Options?.StopSequences
        };

        if (request.Options?.Tools != null && request.Options.Tools.Count > 0)
        {
            providerRequest.Tools = request.Options.Tools.Select(t => new OpenAIResponsesToolDefinition
            {
                Type = "function",
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }).ToList();
        }

        return providerRequest;
    }

    private OpenAIResponsesInputItem MapToInputItem(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };

        return new OpenAIResponsesInputItem
        {
            Role = role,
            Content = BuildTextContent(message),
            ToolCallId = message.ToolCallId
        };
    }

    private static string? BuildTextContent(ChatMessage message)
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

    private static string MapContentPartToText(ContentPart part)
        => part switch
        {
            TextPart text => text.Text,
            ImageUrlPart image => $"[image: {image.Url}]",
            BinaryPart binary => $"[binary: {binary.MediaType}, {binary.Data.Length} bytes]",
            _ => "[unsupported content part]"
        };

    private ChatResponse MapFromResponse(OpenAIResponsesResponse? response)
    {
        if (response == null)
        {
            return new ChatResponse { IsSuccess = false, ErrorMessage = "Invalid response from OpenAI Responses API" };
        }

        var outputText = string.Empty;
        IReadOnlyList<ToolCall>? toolCalls = null;
        var toolCallList = new List<ToolCall>();

        if (response.Output != null)
        {
            foreach (var item in response.Output)
            {
                if (item.Type == "message" && item.Content != null)
                {
                    foreach (var content in item.Content)
                    {
                        if (content.Type == "text" && !string.IsNullOrEmpty(content.Text))
                        {
                            outputText += content.Text;
                        }
                    }
                }
                else if (item.Type == "function_call")
                {
                    toolCallList.Add(new ToolCall
                    {
                        Id = item.Id ?? string.Empty,
                        Name = item.Name ?? string.Empty,
                        Arguments = item.Arguments ?? string.Empty
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
            FinishReason = response.Status,
            Usage = response.Usage == null ? null : new UsageInfo
            {
                PromptTokens = response.Usage.InputTokens,
                CompletionTokens = response.Usage.OutputTokens,
                TotalTokens = response.Usage.TotalTokens
            }
        };
    }
}

public class OpenAIResponsesRequest
{
    public string Model { get; set; } = string.Empty;
    public bool? Stream { get; set; }
    public List<OpenAIResponsesInputItem> Input { get; set; } = [];
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public IReadOnlyList<string>? Stop { get; set; }
    public List<OpenAIResponsesToolDefinition>? Tools { get; set; }
}

public class OpenAIResponsesInputItem
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ToolCallId { get; set; }
}

public class OpenAIResponsesToolDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Parameters { get; set; }
}

public class OpenAIResponsesResponse
{
    public string? Status { get; set; }
    public List<OpenAIResponsesOutputItem>? Output { get; set; }
    public OpenAIResponsesUsage? Usage { get; set; }
}

public class OpenAIResponsesOutputItem
{
    public string Type { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Arguments { get; set; }
    public List<OpenAIResponsesContentItem>? Content { get; set; }
}

public class OpenAIResponsesContentItem
{
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
}

public class OpenAIResponsesUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
}

public class OpenAIResponsesStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public string? Delta { get; set; }
    public OpenAIResponsesOutputItem? Item { get; set; }
    public OpenAIResponsesUsage? Usage { get; set; }
}

internal class ToolCallAccumulator
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string Arguments { get; set; } = string.Empty;
}
