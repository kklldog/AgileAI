using Microsoft.Extensions.Logging;
using AgileAI.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgileAI.Core;

public class ChatSession : IChatSession
{
    private readonly IChatClient _chatClient;
    private readonly string _modelId;
    private readonly List<ChatMessage> _history = new();
    private readonly IToolRegistry? _toolRegistry;
    private readonly int _maxToolLoopIterations;
    private readonly IToolExecutionGate _toolExecutionGate;
    private readonly string? _sessionId;
    private readonly string? _conversationId;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<ChatSession>? _logger;
    private readonly ToolExecutor _toolExecutor;

    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();

    public ChatSession(
        IChatClient chatClient,
        string modelId,
        IToolRegistry? toolRegistry = null,
        int maxToolLoopIterations = 5,
        IToolExecutionGate? toolExecutionGate = null,
        string? sessionId = null,
        string? conversationId = null,
        IServiceProvider? serviceProvider = null,
        ILogger<ChatSession>? logger = null)
    {
        _chatClient = chatClient;
        _modelId = modelId;
        _toolRegistry = toolRegistry;
        _maxToolLoopIterations = maxToolLoopIterations;
        _toolExecutionGate = toolExecutionGate ?? new AutoApproveToolExecutionGate();
        _sessionId = sessionId;
        _conversationId = conversationId;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _toolExecutor = new ToolExecutor(_toolExecutionGate, serviceProvider?.GetService(typeof(ILogger<ToolExecutor>)) as ILogger<ToolExecutor>);
    }

    public void AddMessage(ChatMessage message)
    {
        _history.Add(message);
    }

    public void ClearHistory()
    {
        _history.Clear();
    }

    public async Task<ChatResponse> SendAsync(string message, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await SendTurnAsync(message, options, cancellationToken);
        return result.Response;
    }

    public async IAsyncEnumerable<ChatTurnStreamUpdate> StreamTurnAsync(
        string message,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AddMessage(ChatMessage.User(message));

        await foreach (var update in ExecuteTurnLoopStreamingAsync(options, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }

    public Task<ChatTurnResult> ContinueAsync(ChatOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteTurnLoopAsync(options, cancellationToken);

    public async IAsyncEnumerable<ChatTurnStreamUpdate> ContinueStreamAsync(
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in ExecuteTurnLoopStreamingAsync(options, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }

    public async Task<ChatTurnResult> SendTurnAsync(string message, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        AddMessage(ChatMessage.User(message));
        return await ExecuteTurnLoopAsync(options, cancellationToken);
    }

    private async Task<ChatTurnResult> ExecuteTurnLoopAsync(ChatOptions? options, CancellationToken cancellationToken)
    {
        var finalOptions = BuildEffectiveOptions(options);

        ChatResponse? lastResponse = null;
            List<ToolResult>? lastToolResults = null;
            List<string>? lastToolNames = null;
        var iteration = 0;

        while (iteration < _maxToolLoopIterations)
        {
            _logger?.LogInformation("ChatSession iteration {Iteration}/{MaxIterations}", iteration + 1, _maxToolLoopIterations);
            
            var request = new ChatRequest
            {
                ModelId = _modelId,
                Messages = _history.AsReadOnly(),
                Options = finalOptions
            };

            lastResponse = await _chatClient.CompleteAsync(request, cancellationToken);

            if (!lastResponse.IsSuccess)
            {
                _logger?.LogError("ChatSession request failed: {ErrorMessage}", lastResponse.ErrorMessage);
                return new ChatTurnResult { Response = lastResponse };
            }

            if (lastResponse.Message != null)
            {
                _history.Add(lastResponse.Message);
            }

            var toolCalls = lastResponse.Message?.ToolCalls;
            if (toolCalls == null || toolCalls.Count == 0 || _toolRegistry == null)
            {
                _logger?.LogInformation("ChatSession completed without tool calls");
                break;
            }

            _logger?.LogInformation("ChatSession executing {ToolCount} tool calls", toolCalls.Count);
            var toolResults = new List<ToolResult>();
            foreach (var toolCall in toolCalls)
            {
                _logger?.LogInformation("Executing tool: {ToolName}", toolCall.Name);
                if (_toolRegistry.TryGetTool(toolCall.Name, out var tool) && tool != null)
                {
                    var context = new ToolExecutionContext
                    {
                        ToolCall = toolCall,
                        ChatHistory = _history.AsReadOnly(),
                        ServiceProvider = _serviceProvider,
                        SessionId = _sessionId,
                        ConversationId = _conversationId
                    };
                    var execution = await _toolExecutor.ExecuteAsync(tool, context, cancellationToken);
                    toolResults.Add(execution.Result);

                    if (execution.PendingApprovalRequest != null)
                    {
                        lastToolResults = toolResults;
                        return new ChatTurnResult
                        {
                            Response = lastResponse,
                            ToolResults = lastToolResults,
                            PendingApprovalRequest = execution.PendingApprovalRequest
                        };
                    }
                }
                else
                {
                    _logger?.LogWarning("Tool '{ToolName}' not found", toolCall.Name);
                    toolResults.Add(new ToolResult
                    {
                        ToolCallId = toolCall.Id,
                        Content = $"Tool '{toolCall.Name}' not found",
                        IsSuccess = false,
                        Status = ToolExecutionStatus.Failed
                    });
                }
            }

            lastToolResults = toolResults;

            foreach (var result in toolResults)
            {
                _history.Add(new ChatMessage
                {
                    Role = ChatRole.Tool,
                    ToolCallId = result.ToolCallId,
                    TextContent = result.Content
                });
            }

            iteration++;
        }

        return new ChatTurnResult
        {
            Response = lastResponse ?? new ChatResponse
            {
                IsSuccess = false,
                ErrorMessage = "No response received"
            },
            ToolResults = lastToolResults
        };
    }

    private ChatOptions BuildEffectiveOptions(ChatOptions? options)
    {
        var finalOptions = options ?? new ChatOptions();
        if (_toolRegistry != null)
        {
            var toolDefs = _toolRegistry.GetToolDefinitions();
            if (toolDefs.Count > 0)
            {
                finalOptions = finalOptions with
                {
                    Tools = toolDefs
                };
            }
        }

        return finalOptions;
    }

    private async IAsyncEnumerable<ChatTurnStreamUpdate> ExecuteTurnLoopStreamingAsync(
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var finalOptions = BuildEffectiveOptions(options);
        List<ToolResult>? lastToolResults = null;
        List<string>? lastToolNames = null;
        var iteration = 0;

        while (iteration < _maxToolLoopIterations)
        {
            _logger?.LogInformation("ChatSession streaming iteration {Iteration}/{MaxIterations}", iteration + 1, _maxToolLoopIterations);

            ChatResponse? response = null;
            await using var streamEnumerator = StreamModelPassAsync(finalOptions, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                string? moveNextError = null;
                try
                {
                    hasNext = await streamEnumerator.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "ChatSession streaming request failed");
                    hasNext = false;
                    moveNextError = ex.Message;
                }

                if (moveNextError != null)
                {
                    yield return new ChatTurnError(moveNextError);
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                var update = streamEnumerator.Current;
                if (update is ChatTurnModelResponse modelResponse)
                {
                    response = modelResponse.Response;
                    continue;
                }

                yield return update;
            }

            if (response == null)
            {
                yield return new ChatTurnError("Model request produced no response.");
                yield break;
            }

            if (!response.IsSuccess)
            {
                _logger?.LogError("ChatSession streaming request failed: {ErrorMessage}", response.ErrorMessage);
                yield return new ChatTurnError(response.ErrorMessage ?? "Model request failed.");
                yield break;
            }

            if (response.Message != null)
            {
                _history.Add(response.Message);
            }

            var toolCalls = response.Message?.ToolCalls;
            if (toolCalls == null || toolCalls.Count == 0 || _toolRegistry == null)
            {
                _logger?.LogInformation("ChatSession streaming completed without tool calls");
                yield return new ChatTurnCompleted(response, lastToolResults, lastToolNames);
                yield break;
            }

            _logger?.LogInformation("ChatSession streaming executing {ToolCount} tool calls", toolCalls.Count);
            var toolResults = new List<ToolResult>();
            var toolNames = new List<string>();
            foreach (var toolCall in toolCalls)
            {
                _logger?.LogInformation("Executing tool: {ToolName}", toolCall.Name);
                toolNames.Add(toolCall.Name);
                if (_toolRegistry.TryGetTool(toolCall.Name, out var tool) && tool != null)
                {
                    var context = new ToolExecutionContext
                    {
                        ToolCall = toolCall,
                        ChatHistory = _history.AsReadOnly(),
                        ServiceProvider = _serviceProvider,
                        SessionId = _sessionId,
                        ConversationId = _conversationId
                    };
                    var execution = await _toolExecutor.ExecuteAsync(tool, context, cancellationToken);
                    toolResults.Add(execution.Result);

                    if (execution.PendingApprovalRequest != null)
                    {
                        lastToolResults = toolResults;
                        lastToolNames = toolNames;
                        yield return new ChatTurnPendingApproval(response, execution.PendingApprovalRequest, lastToolResults, lastToolNames);
                        yield break;
                    }
                }
                else
                {
                    _logger?.LogWarning("Tool '{ToolName}' not found", toolCall.Name);
                    toolResults.Add(new ToolResult
                    {
                        ToolCallId = toolCall.Id,
                        Content = $"Tool '{toolCall.Name}' not found",
                        IsSuccess = false,
                        Status = ToolExecutionStatus.Failed
                    });
                }
            }

            lastToolResults = toolResults;
            lastToolNames = toolNames;

            foreach (var result in toolResults)
            {
                _history.Add(new ChatMessage
                {
                    Role = ChatRole.Tool,
                    ToolCallId = result.ToolCallId,
                    TextContent = result.Content
                });
            }

            iteration++;
        }

        yield return new ChatTurnCompleted(new ChatResponse
        {
            IsSuccess = false,
            ErrorMessage = "Maximum tool loop iterations reached"
        }, lastToolResults, lastToolNames);
    }

    private async IAsyncEnumerable<ChatTurnStreamUpdate> StreamModelPassAsync(
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new ChatRequest
        {
            ModelId = _modelId,
            Messages = _history.AsReadOnly(),
            Options = options
        };

        var assistantBuilder = new StringBuilder();
        string? finishReason = null;
        UsageInfo? usage = null;
        var toolCallAccumulators = new Dictionary<string, ToolCallAccumulator>(StringComparer.Ordinal);
        var toolCallOrder = new List<string>();

        await foreach (var update in _chatClient.StreamAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            switch (update)
            {
                case TextDeltaUpdate textDelta:
                    assistantBuilder.Append(textDelta.Delta);
                    yield return new ChatTurnTextDelta(textDelta.Delta);
                    break;
                case ToolCallDeltaUpdate toolCallDelta:
                    var toolCallId = string.IsNullOrWhiteSpace(toolCallDelta.ToolCallId)
                        ? $"tool-call-{toolCallOrder.Count + 1}"
                        : toolCallDelta.ToolCallId;
                    if (!toolCallAccumulators.TryGetValue(toolCallId, out var accumulator))
                    {
                        accumulator = new ToolCallAccumulator(toolCallId);
                        toolCallAccumulators[toolCallId] = accumulator;
                        toolCallOrder.Add(toolCallId);
                    }

                    if (!string.IsNullOrWhiteSpace(toolCallDelta.NameDelta))
                    {
                        accumulator.Name.Append(toolCallDelta.NameDelta);
                    }

                    if (!string.IsNullOrWhiteSpace(toolCallDelta.ArgumentsDelta))
                    {
                        accumulator.Arguments.Append(toolCallDelta.ArgumentsDelta);
                    }
                    break;
                case UsageUpdate usageUpdate:
                    usage = usageUpdate.Usage;
                    yield return new ChatTurnUsage(usageUpdate.Usage);
                    break;
                case CompletedUpdate completedUpdate:
                    finishReason = completedUpdate.FinishReason;
                    break;
                case ErrorUpdate errorUpdate:
                    throw new InvalidOperationException(errorUpdate.ErrorMessage);
            }
        }

        var toolCalls = toolCallOrder.Count == 0
            ? null
            : toolCallOrder.Select(id => toolCallAccumulators[id].Build()).ToList();

        yield return new ChatTurnModelResponse(new ChatResponse
        {
            IsSuccess = true,
            Message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                TextContent = assistantBuilder.ToString(),
                ToolCalls = toolCalls
            },
            Usage = usage,
            FinishReason = finishReason
        });
    }

    private sealed class ToolCallAccumulator(string id)
    {
        public string Id { get; } = id;
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();

        public ToolCall Build()
            => new()
            {
                Id = Id,
                Name = Name.ToString(),
                Arguments = Arguments.ToString()
            };
    }
}
