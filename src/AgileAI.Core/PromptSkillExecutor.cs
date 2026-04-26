using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Core;

public class PromptSkillExecutor : ISkillExecutor
{
    private readonly IChatClient _chatClient;
    private readonly IToolRegistry? _toolRegistry;
    private readonly ILogger<PromptSkillExecutor>? _logger;
    private readonly IReadOnlyList<IChatTurnMiddleware> _chatTurnMiddlewares;

    public PromptSkillExecutor(
        IChatClient chatClient,
        IToolRegistry? toolRegistry = null,
        ILogger<PromptSkillExecutor>? logger = null,
        IEnumerable<IChatTurnMiddleware>? chatTurnMiddlewares = null)
    {
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _chatTurnMiddlewares = chatTurnMiddlewares?.ToList() ?? [];
    }

    public async Task<AgentResult> ExecuteAsync(
        SkillManifest manifest,
        SkillExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var modelId = context.ModelId ?? context.Request.ModelId ?? throw new InvalidOperationException("ModelId is required");

        var preparedHistory = SkillPromptHelper.PrepareHistoryForSkill(context.Request.History, manifest).ToList();
        var originalHistoryCount = context.Request.History?.Count ?? 0;
        var insertedMessages = preparedHistory.Count - originalHistoryCount;

        var messages = preparedHistory;
        messages.Add(ChatMessage.User(context.Request.Input));

        var options = context.Request.Options ?? new ChatOptions();
        var toolCount = 0;
        if (_toolRegistry != null)
        {
            var tools = _toolRegistry.GetToolDefinitions();
            toolCount = tools.Count;
            if (toolCount > 0)
            {
                options = options with { Tools = tools };
            }
        }

        _logger?.LogInformation(
            "Executing local skill {SkillName}. ModelId={ModelId}, OriginalHistory={OriginalHistory}, PreparedHistory={PreparedHistory}, InsertedMessages={InsertedMessages}, ToolCount={ToolCount}",
            manifest.Name,
            modelId,
            originalHistoryCount,
            preparedHistory.Count,
            insertedMessages,
            toolCount);

        var middlewareContext = new ChatTurnExecutionContext
        {
            Kind = ChatTurnExecutionKind.PromptSkill,
            ModelId = modelId,
            Input = context.Request.Input,
            Messages = messages.AsReadOnly(),
            Options = options,
            ServiceProvider = context.ServiceProvider
        };

        var turnResult = await MiddlewarePipeline.ExecuteAsync(
            _chatTurnMiddlewares,
            middlewareContext,
            static (middleware, executionContext, next, ct) => middleware.InvokeAsync(executionContext, next, ct),
            async () => new ChatTurnResult
            {
                Response = await _chatClient.CompleteAsync(new ChatRequest
                {
                    ModelId = modelId,
                    Messages = messages,
                    Options = middlewareContext.Options
                }, cancellationToken)
            },
            cancellationToken);

        var response = turnResult.Response;

        _logger?.LogInformation(
            "Local skill execution completed. Skill={SkillName}, Success={Success}, Error={Error}",
            manifest.Name,
            response.IsSuccess,
            response.ErrorMessage);

        return new AgentResult
        {
            IsSuccess = response.IsSuccess,
            Output = response.Message?.TextContent ?? string.Empty,
            ErrorMessage = response.ErrorMessage,
            UpdatedHistory = messages.Concat(response.Message != null ? [response.Message] : []).ToList()
        };
    }
}
