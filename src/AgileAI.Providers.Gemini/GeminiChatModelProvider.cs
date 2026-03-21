using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Providers.Gemini;

public class GeminiChatModelProvider : IChatModelProvider
{
    public string ProviderName => "gemini";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<GeminiChatModelProvider>? _logger;

    public GeminiChatModelProvider(
        HttpClient httpClient,
        GeminiOptions options,
        ILogger<GeminiChatModelProvider>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        httpClient.BaseAddress = new Uri(options.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta/");
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting Gemini request {RequestId} to model {ModelId}", requestId, request.ModelId);

        try
        {
            var providerRequest = CreateProviderRequest(request);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var modelId = request.ModelId.StartsWith("gemini:") ? request.ModelId.Substring("gemini:".Length) : request.ModelId;
            var response = await _httpClient.PostAsync($"models/{modelId}:generateContent?key={_httpClient.DefaultRequestHeaders.GetValues("x-goog-api-key").First()}", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var providerResponse = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseJson, _jsonOptions);
            _logger?.LogInformation("Gemini request {RequestId} completed successfully", requestId);
            return MapFromResponse(providerResponse);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Gemini request {RequestId} failed", requestId);
            return new ChatResponse { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async IAsyncEnumerable<StreamingChatUpdate> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger?.LogInformation("Starting streaming Gemini request {RequestId} to model {ModelId}", requestId, request.ModelId);

        HttpResponseMessage? response = null;
        Exception? initialException = null;

        try
        {
            var providerRequest = CreateProviderRequest(request);
            var json = JsonSerializer.Serialize(providerRequest, _jsonOptions);
            var modelId = request.ModelId.StartsWith("gemini:") ? request.ModelId.Substring("gemini:".Length) : request.ModelId;
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"models/{modelId}:streamGenerateContent?key={_httpClient.DefaultRequestHeaders.GetValues("x-goog-api-key").First()}&alt=sse")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Streaming Gemini request {RequestId} failed during initialization", requestId);
            initialException = ex;
        }

        if (initialException != null)
        {
            yield return new ErrorUpdate(initialException.Message);
            yield break;
        }

        if (response == null)
            yield break;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("data: "))
                line = line["data: ".Length..];

            GeminiGenerateContentResponse? streamResponse;
            try
            {
                streamResponse = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(line, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize streaming line");
                continue;
            }

            if (streamResponse?.Candidates != null && streamResponse.Candidates.Count > 0)
            {
                var candidate = streamResponse.Candidates[0];
                if (candidate.Content?.Parts != null)
                {
                    foreach (var part in candidate.Content.Parts)
                    {
                        if (!string.IsNullOrEmpty(part.Text))
                            yield return new TextDeltaUpdate(part.Text);
                    }
                }

                if (!string.IsNullOrEmpty(candidate.FinishReason))
                    yield return new CompletedUpdate(candidate.FinishReason);
            }

            if (streamResponse?.UsageMetadata != null)
            {
                yield return new UsageUpdate(new UsageInfo
                {
                    PromptTokens = streamResponse.UsageMetadata.PromptTokenCount,
                    CompletionTokens = streamResponse.UsageMetadata.CandidatesTokenCount,
                    TotalTokens = streamResponse.UsageMetadata.TotalTokenCount
                });
            }
        }

        _logger?.LogInformation("Streaming Gemini request {RequestId} completed", requestId);
    }

    private GeminiGenerateContentRequest CreateProviderRequest(ChatRequest request)
    {
        var providerRequest = new GeminiGenerateContentRequest
        {
            Contents = request.Messages.Select(MapToContent).ToList(),
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = request.Options?.Temperature,
                TopP = request.Options?.TopP,
                MaxOutputTokens = request.Options?.MaxTokens,
                StopSequences = request.Options?.StopSequences
            }
        };

        if (request.Options?.Tools != null && request.Options.Tools.Count > 0)
        {
            providerRequest.Tools = new List<GeminiTool>
            {
                new GeminiTool
                {
                    FunctionDeclarations = request.Options.Tools.Select(t => new GeminiFunctionDeclaration
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = t.ParametersSchema
                    }).ToList()
                }
            };
        }

        return providerRequest;
    }

    private GeminiContent MapToContent(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.User => "user",
            ChatRole.Assistant => "model",
            _ => "user"
        };

        var parts = new List<GeminiPart>();
        if (!string.IsNullOrEmpty(message.TextContent))
        {
            parts.Add(new GeminiPart { Text = message.TextContent });
        }
        else if (message.ContentParts != null && message.ContentParts.Count > 0)
        {
            foreach (var part in message.ContentParts)
            {
                switch (part)
                {
                    case TextPart textPart:
                        parts.Add(new GeminiPart { Text = textPart.Text });
                        break;
                    case ImageUrlPart imagePart:
                        parts.Add(new GeminiPart { Text = $"[image: {imagePart.Url}]" });
                        break;
                    case BinaryPart binaryPart:
                        parts.Add(new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = binaryPart.MediaType,
                                Data = Convert.ToBase64String(binaryPart.Data)
                            }
                        });
                        break;
                }
            }
        }

        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
        {
            foreach (var toolCall in message.ToolCalls)
            {
                parts.Add(new GeminiPart
                {
                    FunctionCall = new GeminiFunctionCall
                    {
                        Name = toolCall.Name,
                        Args = JsonSerializer.Deserialize<Dictionary<string, object>>(toolCall.Arguments)
                    }
                });
            }
        }

        return new GeminiContent
        {
            Role = role,
            Parts = parts
        };
    }

    private ChatResponse MapFromResponse(GeminiGenerateContentResponse? response)
    {
        if (response?.Candidates == null || response.Candidates.Count == 0)
        {
            return new ChatResponse { IsSuccess = false, ErrorMessage = "Invalid response from Gemini" };
        }

        var candidate = response.Candidates[0];
        var outputText = string.Empty;
        IReadOnlyList<ToolCall>? toolCalls = null;
        var toolCallList = new List<ToolCall>();

        if (candidate.Content?.Parts != null)
        {
            foreach (var part in candidate.Content.Parts)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    outputText += part.Text;
                }
                else if (part.FunctionCall != null)
                {
                    toolCallList.Add(new ToolCall
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = part.FunctionCall.Name ?? string.Empty,
                        Arguments = JsonSerializer.Serialize(part.FunctionCall.Args)
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
            FinishReason = candidate.FinishReason,
            Usage = response.UsageMetadata == null ? null : new UsageInfo
            {
                PromptTokens = response.UsageMetadata.PromptTokenCount,
                CompletionTokens = response.UsageMetadata.CandidatesTokenCount,
                TotalTokens = response.UsageMetadata.TotalTokenCount
            }
        };
    }
}

public class GeminiGenerateContentRequest
{
    public List<GeminiContent> Contents { get; set; } = [];
    public GeminiGenerationConfig? GenerationConfig { get; set; }
    public List<GeminiTool>? Tools { get; set; }
}

public class GeminiContent
{
    public string Role { get; set; } = string.Empty;
    public List<GeminiPart> Parts { get; set; } = [];
}

public class GeminiPart
{
    public string? Text { get; set; }
    public GeminiFunctionCall? FunctionCall { get; set; }
    public GeminiInlineData? InlineData { get; set; }
}

public class GeminiInlineData
{
    public string MimeType { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}

public class GeminiFunctionCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Args { get; set; }
}

public class GeminiGenerationConfig
{
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
}

public class GeminiTool
{
    public List<GeminiFunctionDeclaration>? FunctionDeclarations { get; set; }
}

public class GeminiFunctionDeclaration
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Parameters { get; set; }
}

public class GeminiGenerateContentResponse
{
    public List<GeminiCandidate>? Candidates { get; set; }
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}

public class GeminiCandidate
{
    public GeminiContent? Content { get; set; }
    public string? FinishReason { get; set; }
}

public class GeminiUsageMetadata
{
    public int PromptTokenCount { get; set; }
    public int CandidatesTokenCount { get; set; }
    public int TotalTokenCount { get; set; }
}
