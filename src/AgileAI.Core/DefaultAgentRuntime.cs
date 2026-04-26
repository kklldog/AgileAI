using AgileAI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgileAI.Core;

public class DefaultAgentRuntime : IAgentRuntime
{
    private readonly IChatClient _chatClient;
    private readonly ISkillRegistry _skillRegistry;
    private readonly IToolRegistry? _toolRegistry;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<DefaultAgentRuntime>? _logger;
    private readonly ISkillPlanner? _skillPlanner;
    private readonly ISessionStore? _sessionStore;
    private readonly ISkillContinuationPolicy? _skillContinuationPolicy;
    private readonly IReadOnlyList<IAgentExecutionMiddleware> _executionMiddlewares;

    public DefaultAgentRuntime(
        IChatClient chatClient,
        ISkillRegistry skillRegistry,
        IToolRegistry? toolRegistry = null,
        IServiceProvider? serviceProvider = null,
        ILogger<DefaultAgentRuntime>? logger = null,
        ISkillPlanner? skillPlanner = null,
        ISessionStore? sessionStore = null,
        ISkillContinuationPolicy? skillContinuationPolicy = null,
        IEnumerable<IAgentExecutionMiddleware>? executionMiddlewares = null)
    {
        _chatClient = chatClient;
        _skillRegistry = skillRegistry;
        _toolRegistry = toolRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _skillPlanner = skillPlanner;
        _sessionStore = sessionStore;
        _skillContinuationPolicy = skillContinuationPolicy;
        _executionMiddlewares = executionMiddlewares?.ToList() ?? [];
    }

    public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        var modelId = request.ModelId ?? throw new InvalidOperationException("ModelId is required");
        _logger?.LogInformation(
            "Runtime execution started. SessionId={SessionId}, EnableSkills={EnableSkills}, PreferredSkill={PreferredSkill}, HistoryCount={HistoryCount}",
            request.SessionId,
            request.EnableSkills,
            request.PreferredSkill,
            request.History?.Count ?? 0);

        var sessionState = await LoadSessionStateAsync(request, cancellationToken);
        var effectiveHistory = request.History ?? sessionState?.History;
        var effectiveRequest = request with { History = effectiveHistory };

        var context = new AgentExecutionContext
        {
            OriginalRequest = request,
            Request = effectiveRequest,
            ModelId = modelId,
            SessionState = sessionState,
            ServiceProvider = _serviceProvider
        };

        return await MiddlewarePipeline.ExecuteAsync(
            _executionMiddlewares,
            context,
            static (middleware, executionContext, next, ct) => middleware.InvokeAsync(executionContext, next, ct),
            () => ExecuteCoreAsync(context, cancellationToken),
            cancellationToken);
    }

    private async Task<AgentResult> ExecuteCoreAsync(AgentExecutionContext context, CancellationToken cancellationToken)
    {
        var effectiveRequest = context.Request;
        var sessionState = context.SessionState;

        ISkill? resolvedSkill = null;
        if (effectiveRequest.EnableSkills)
        {
            resolvedSkill = await ResolveSkillAsync(effectiveRequest, sessionState, cancellationToken);
            if (resolvedSkill != null)
            {
                _logger?.LogInformation("Executing request with resolved skill {SkillName}", resolvedSkill.Name);
                var result = await resolvedSkill.ExecuteAsync(CreateSkillContext(effectiveRequest, context.ModelId, resolvedSkill), cancellationToken);
                await PersistSessionStateAsync(effectiveRequest, sessionState, result.UpdatedHistory, resolvedSkill.Name, cancellationToken);

                _logger?.LogInformation(
                    "Runtime execution finished with skill {SkillName}. Success={Success}, OutputLength={OutputLength}, UpdatedHistoryCount={UpdatedHistoryCount}",
                    resolvedSkill.Name,
                    result.IsSuccess,
                    result.Output?.Length ?? 0,
                    result.UpdatedHistory?.Count ?? 0);
                return result;
            }

            _logger?.LogInformation("No skill selected. Falling back to plain chat execution.");
        }

        var plainResult = await ExecutePlainChatAsync(effectiveRequest, context.ModelId, cancellationToken);
        await PersistSessionStateAsync(effectiveRequest, sessionState, plainResult.UpdatedHistory, activeSkill: null, cancellationToken);

        _logger?.LogInformation(
            "Runtime execution finished with plain chat. Success={Success}, OutputLength={OutputLength}, UpdatedHistoryCount={UpdatedHistoryCount}",
            plainResult.IsSuccess,
            plainResult.Output?.Length ?? 0,
            plainResult.UpdatedHistory?.Count ?? 0);
        return plainResult;
    }

    private async Task<ISkill?> ResolveSkillAsync(AgentRequest request, ConversationState? sessionState, CancellationToken cancellationToken)
    {
        var allowed = request.AllowedSkills is { Count: > 0 }
            ? new HashSet<string>(request.AllowedSkills, StringComparer.OrdinalIgnoreCase)
            : null;

        if (!string.IsNullOrWhiteSpace(request.PreferredSkill) &&
            _skillRegistry.TryGetSkill(request.PreferredSkill, out var preferred) &&
            preferred != null &&
            (allowed == null || allowed.Contains(preferred.Name)))
        {
            _logger?.LogInformation("Resolved skill from PreferredSkill: {SkillName}", preferred.Name);
            return preferred;
        }

        var skills = _skillRegistry.GetAllSkills() ?? [];
        _logger?.LogDebug("Skill resolution started. RegisteredSkills={SkillCount}", skills.Count);
        if (skills.Count == 0)
        {
            _logger?.LogWarning("Skill resolution skipped: no registered skills available.");
            return null;
        }

        if (_skillContinuationPolicy != null)
        {
            var continuationDecision = await _skillContinuationPolicy.DecideAsync(request, sessionState, skills, cancellationToken);
            _logger?.LogInformation(
                "Continuation policy decision. ContinueActiveSkill={ContinueActiveSkill}, SkillName={SkillName}, Reason={Reason}",
                continuationDecision.ContinueActiveSkill,
                continuationDecision.SkillName,
                continuationDecision.Reason);

            if (continuationDecision.ContinueActiveSkill &&
                !string.IsNullOrWhiteSpace(continuationDecision.SkillName) &&
                _skillRegistry.TryGetSkill(continuationDecision.SkillName, out var continuedSkill) &&
                continuedSkill != null &&
                (allowed == null || allowed.Contains(continuedSkill.Name)))
            {
                _logger?.LogInformation("Resolved skill from continuation policy: {SkillName}", continuedSkill.Name);
                return continuedSkill;
            }
        }

        if (_skillPlanner == null)
        {
            _logger?.LogDebug("Skill planner unavailable. Skip planner-based skill selection.");
            return null;
        }

        var plan = await _skillPlanner.PlanAsync(request, skills, cancellationToken);
        if (plan == null || !plan.ShouldUseSkill || string.IsNullOrWhiteSpace(plan.SkillName))
        {
            _logger?.LogInformation("Planner decided not to use skill.");
            return null;
        }

        var planned = _skillRegistry.TryGetSkill(plan.SkillName, out var skill) ? skill : null;
        if (planned != null)
        {
            _logger?.LogInformation("Resolved skill from planner: {SkillName}", planned.Name);
        }
        else
        {
            _logger?.LogWarning("Planner selected skill {SkillName} but it is not registered.", plan.SkillName);
        }

        return planned;
    }

    private async Task<AgentResult> ExecutePlainChatAsync(AgentRequest request, string modelId, CancellationToken cancellationToken)
    {
        var chatSession = new ChatSession(
            _chatClient,
            modelId,
            _toolRegistry,
            serviceProvider: _serviceProvider,
            logger: _serviceProvider?.GetService<ILogger<ChatSession>>());

        if (request.History != null)
        {
            foreach (var message in request.History)
            {
                chatSession.AddMessage(message);
            }
        }

        _logger?.LogDebug("Executing plain chat. SeedHistoryCount={HistoryCount}", request.History?.Count ?? 0);
        var response = await chatSession.SendAsync(request.Input, request.Options, cancellationToken);

        return new AgentResult
        {
            IsSuccess = response.IsSuccess,
            Output = response.Message?.TextContent ?? string.Empty,
            ErrorMessage = response.ErrorMessage,
            UpdatedHistory = chatSession.History
        };
    }

    private SkillExecutionContext CreateSkillContext(AgentRequest request, string modelId, ISkill skill)
    {
        return new SkillExecutionContext
        {
            Request = request,
            ServiceProvider = _serviceProvider,
            ModelId = modelId,
            History = request.History,
            SkillRootDirectory = skill.Manifest?.RootDirectory,
            SkillMarkdownPath = skill.Manifest?.SkillMarkdownPath,
            Items = request.Metadata?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, object?>()
        };
    }

    private async Task<ConversationState?> LoadSessionStateAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (_sessionStore == null || string.IsNullOrWhiteSpace(request.SessionId))
        {
            return null;
        }

        var state = await _sessionStore.GetAsync(request.SessionId, cancellationToken);
        _logger?.LogInformation(
            "Session state loaded. SessionId={SessionId}, Found={Found}, HistoryCount={HistoryCount}, ActiveSkill={ActiveSkill}",
            request.SessionId,
            state != null,
            state?.History?.Count ?? 0,
            state?.ActiveSkill);
        return state;
    }

    private async Task PersistSessionStateAsync(
        AgentRequest request,
        ConversationState? existingState,
        IReadOnlyList<ChatMessage>? updatedHistory,
        string? activeSkill,
        CancellationToken cancellationToken)
    {
        if (_sessionStore == null || string.IsNullOrWhiteSpace(request.SessionId) || updatedHistory == null)
        {
            return;
        }

        var state = new ConversationState
        {
            SessionId = request.SessionId,
            History = updatedHistory,
            ActiveSkill = activeSkill,
            Metadata = request.Metadata ?? existingState?.Metadata ?? new Dictionary<string, object?>(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _sessionStore.SaveAsync(state, cancellationToken);
        _logger?.LogInformation(
            "Session state persisted. SessionId={SessionId}, HistoryCount={HistoryCount}, ActiveSkill={ActiveSkill}",
            state.SessionId,
            state.History?.Count ?? 0,
            state.ActiveSkill);
    }
}
