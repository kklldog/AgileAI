using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Core;

public class ChatSessionBuilder(IChatClient chatClient, string modelId)
{
    private IToolRegistry? _toolRegistry;
    private int _maxToolLoopIterations = 5;
    private int _maxConsecutiveToolFailures = 3;
    private IServiceProvider? _serviceProvider;
    private ILogger<ChatSession>? _logger;
    private IReadOnlyList<ChatMessage> _history = [];
    private IToolExecutionGate? _toolExecutionGate;
    private string? _sessionId;
    private string? _conversationId;
    private IReadOnlyList<IChatTurnMiddleware>? _chatTurnMiddlewares;
    private IReadOnlyList<IStreamingChatTurnMiddleware>? _streamingChatTurnMiddlewares;
    private IReadOnlyList<IToolExecutionMiddleware>? _toolExecutionMiddlewares;

    public ChatSessionBuilder WithToolRegistry(IToolRegistry? toolRegistry)
    {
        _toolRegistry = toolRegistry;
        return this;
    }

    public ChatSessionBuilder WithMaxToolLoopIterations(int maxToolLoopIterations)
    {
        _maxToolLoopIterations = maxToolLoopIterations;
        return this;
    }

    public ChatSessionBuilder WithMaxConsecutiveToolFailures(int maxConsecutiveToolFailures)
    {
        _maxConsecutiveToolFailures = maxConsecutiveToolFailures;
        return this;
    }

    public ChatSessionBuilder WithServiceProvider(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
        return this;
    }

    public ChatSessionBuilder UseServiceProvider(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
        return this;
    }

    public ChatSessionBuilder WithLogger(ILogger<ChatSession>? logger)
    {
        _logger = logger;
        return this;
    }

    public ChatSessionBuilder WithToolExecutionGate(IToolExecutionGate? toolExecutionGate)
    {
        _toolExecutionGate = toolExecutionGate;
        return this;
    }

    public ChatSessionBuilder WithSessionId(string? sessionId)
    {
        _sessionId = sessionId;
        return this;
    }

    public ChatSessionBuilder WithConversationId(string? conversationId)
    {
        _conversationId = conversationId;
        return this;
    }

    public ChatSessionBuilder WithChatTurnMiddleware(IEnumerable<IChatTurnMiddleware> chatTurnMiddlewares)
    {
        _chatTurnMiddlewares = chatTurnMiddlewares.ToList();
        return this;
    }

    public ChatSessionBuilder WithStreamingChatTurnMiddleware(IEnumerable<IStreamingChatTurnMiddleware> streamingChatTurnMiddlewares)
    {
        _streamingChatTurnMiddlewares = streamingChatTurnMiddlewares.ToList();
        return this;
    }

    public ChatSessionBuilder WithToolExecutionMiddleware(IEnumerable<IToolExecutionMiddleware> toolExecutionMiddlewares)
    {
        _toolExecutionMiddlewares = toolExecutionMiddlewares.ToList();
        return this;
    }

    public ChatSessionBuilder WithHistory(IEnumerable<ChatMessage> history)
    {
        _history = history.ToList();
        return this;
    }

    public ChatSession Build()
    {
        var session = new ChatSession(
            chatClient,
            modelId,
            _toolRegistry,
            _maxToolLoopIterations,
            _toolExecutionGate,
            _sessionId,
            _conversationId,
            _serviceProvider,
            _logger,
            _chatTurnMiddlewares,
            _streamingChatTurnMiddlewares,
            _toolExecutionMiddlewares,
            _maxConsecutiveToolFailures);

        foreach (var message in _history)
        {
            session.AddMessage(message);
        }

        return session;
    }
}
