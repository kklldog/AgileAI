using AgileAI.Abstractions;
using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Providers.OpenAICompatible;
using Microsoft.EntityFrameworkCore;

namespace AgileAI.Studio.Api.Services;

public class ModelCatalogService(StudioDbContext dbContext, ProviderClientFactory providerClientFactory)
{
    public async Task<IReadOnlyList<ProviderConnectionDto>> GetProviderConnectionsAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.ProviderConnections
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(x => x.UpdatedAtUtc.UtcDateTime)
            .Select(MapProviderConnection)
            .ToList();
    }

    public async Task<ProviderConnectionDto> CreateProviderConnectionAsync(ProviderConnectionRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new ProviderConnection
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            ProviderType = request.ProviderType,
            ApiKey = request.ApiKey.Trim(),
            BaseUrl = NormalizeOptional(request.BaseUrl),
            Endpoint = NormalizeOptional(request.Endpoint),
            ProviderName = NormalizeOptional(request.ProviderName),
            RelativePath = NormalizeOptional(request.RelativePath),
            ApiKeyHeaderName = NormalizeOptional(request.ApiKeyHeaderName),
            AuthMode = NormalizeOptional(request.AuthMode),
            ApiVersion = NormalizeOptional(request.ApiVersion),
            IsEnabled = request.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ValidateProviderConnection(entity);
        dbContext.ProviderConnections.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapProviderConnection(entity);
    }

    public async Task<ProviderConnectionDto> UpdateProviderConnectionAsync(Guid id, ProviderConnectionRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.ProviderConnections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Provider connection not found.");

        entity.Name = request.Name.Trim();
        entity.ProviderType = request.ProviderType;
        entity.ApiKey = request.ApiKey.Trim();
        entity.BaseUrl = NormalizeOptional(request.BaseUrl);
        entity.Endpoint = NormalizeOptional(request.Endpoint);
        entity.ProviderName = NormalizeOptional(request.ProviderName);
        entity.RelativePath = NormalizeOptional(request.RelativePath);
        entity.ApiKeyHeaderName = NormalizeOptional(request.ApiKeyHeaderName);
        entity.AuthMode = NormalizeOptional(request.AuthMode);
        entity.ApiVersion = NormalizeOptional(request.ApiVersion);
        entity.IsEnabled = request.IsEnabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        ValidateProviderConnection(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapProviderConnection(entity);
    }

    public async Task DeleteProviderConnectionAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.ProviderConnections
            .Include(x => x.Models)
            .ThenInclude(x => x.Agents)
            .ThenInclude(x => x.Conversations)
            .ThenInclude(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Provider connection not found.");

        RemoveDependentModels(entity.Models);

        dbContext.ProviderConnections.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ModelDto>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.Models
            .Include(x => x.ProviderConnection)
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(x => x.UpdatedAtUtc.UtcDateTime)
            .Select(MapModel)
            .ToList();
    }

    public async Task<ModelDto> CreateModelAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var provider = await dbContext.ProviderConnections.FirstOrDefaultAsync(x => x.Id == request.ProviderConnectionId, cancellationToken)
            ?? throw new InvalidOperationException("Provider connection not found.");

        var now = DateTimeOffset.UtcNow;
        var entity = new StudioModel
        {
            Id = Guid.NewGuid(),
            ProviderConnectionId = provider.Id,
            DisplayName = request.DisplayName.Trim(),
            ModelKey = request.ModelKey.Trim(),
            SupportsStreaming = request.SupportsStreaming,
            SupportsTools = request.SupportsTools,
            SupportsVision = request.SupportsVision,
            IsEnabled = request.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ValidateModel(entity);
        dbContext.Models.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        entity.ProviderConnection = provider;
        return MapModel(entity);
    }

    public async Task<ModelDto> UpdateModelAsync(Guid id, ModelRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Models.Include(x => x.ProviderConnection).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Model not found.");
        var provider = await dbContext.ProviderConnections.FirstOrDefaultAsync(x => x.Id == request.ProviderConnectionId, cancellationToken)
            ?? throw new InvalidOperationException("Provider connection not found.");

        entity.ProviderConnectionId = provider.Id;
        entity.ProviderConnection = provider;
        entity.DisplayName = request.DisplayName.Trim();
        entity.ModelKey = request.ModelKey.Trim();
        entity.SupportsStreaming = request.SupportsStreaming;
        entity.SupportsTools = request.SupportsTools;
        entity.SupportsVision = request.SupportsVision;
        entity.IsEnabled = request.IsEnabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        ValidateModel(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapModel(entity);
    }

    public async Task DeleteModelAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Models
            .Include(x => x.Agents)
            .ThenInclude(x => x.Conversations)
            .ThenInclude(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Model not found.");

        RemoveDependentAgents(entity.Agents);

        dbContext.Models.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void RemoveDependentModels(IEnumerable<StudioModel> models)
    {
        foreach (var model in models.ToList())
        {
            RemoveDependentAgents(model.Agents);
            dbContext.Models.Remove(model);
        }
    }

    private void RemoveDependentAgents(IEnumerable<AgentDefinition> agents)
    {
        foreach (var agent in agents.ToList())
        {
            RemoveDependentConversations(agent.Conversations);
            dbContext.Agents.Remove(agent);
        }
    }

    private void RemoveDependentConversations(IEnumerable<Conversation> conversations)
    {
        foreach (var conversation in conversations.ToList())
        {
            dbContext.Messages.RemoveRange(conversation.Messages);
            dbContext.Conversations.Remove(conversation);
        }
    }

    public async Task<ProviderRuntimeOptions> GetRuntimeOptionsAsync(Guid studioModelId, CancellationToken cancellationToken)
    {
        var model = await dbContext.Models.Include(x => x.ProviderConnection).FirstOrDefaultAsync(x => x.Id == studioModelId, cancellationToken)
            ?? throw new InvalidOperationException("Model not found.");

        if (model.ProviderConnection == null)
        {
            throw new InvalidOperationException("Provider connection missing for model.");
        }

        var provider = model.ProviderConnection;
        ValidateProviderConnection(provider);

        var runtimeProviderName = provider.ProviderType switch
        {
            ProviderType.OpenAI => "openai",
            ProviderType.AzureOpenAI => "azure-openai",
            ProviderType.OpenAICompatible => NormalizeProviderName(provider.ProviderName),
            ProviderType.DeepSeek => DeepSeekProviderName,
            _ => throw new InvalidOperationException("Unsupported provider type.")
        };

        OpenAICompatibleAuthMode? authMode = provider.ProviderType is ProviderType.OpenAICompatible or ProviderType.DeepSeek
            ? ParseAuthMode(provider.AuthMode)
            : null;

        var baseUrl = provider.ProviderType == ProviderType.DeepSeek
            ? provider.BaseUrl ?? DeepSeekBaseUrl
            : provider.BaseUrl;
        var relativePath = provider.ProviderType == ProviderType.DeepSeek
            ? provider.RelativePath ?? DeepSeekRelativePath
            : provider.RelativePath;

        return new ProviderRuntimeOptions(
            provider.ProviderType,
            runtimeProviderName,
            $"{runtimeProviderName}:{model.ModelKey}",
            provider.ApiKey,
            baseUrl,
            provider.Endpoint,
            relativePath,
            authMode,
            provider.ApiKeyHeaderName,
            provider.ApiVersion);
    }

    public async Task<ConnectionTestResultDto> TestModelAsync(Guid studioModelId, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeOptionsAsync(studioModelId, cancellationToken);
        var chatClient = providerClientFactory.CreateClient(runtime);

        try
        {
            var response = await chatClient.CompleteAsync(new ChatRequest
            {
                ModelId = runtime.RuntimeModelId,
                Messages = [
                    ChatMessage.System("You are a connectivity check. Reply with the single word OK."),
                    ChatMessage.User("Respond with OK")
                ],
                Options = new ChatOptions
                {
                    Temperature = 0,
                    MaxTokens = 16
                }
            }, cancellationToken);

            if (!response.IsSuccess)
            {
                return new ConnectionTestResultDto(false, response.ErrorMessage ?? "Model connectivity test failed.");
            }

            var output = response.Message?.TextContent?.Trim();
            var preview = string.IsNullOrWhiteSpace(output) ? "Connection succeeded." : $"Connection succeeded. Model replied: {output}";
            return new ConnectionTestResultDto(true, preview);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResultDto(false, $"Connection failed: {ex.Message}");
        }
    }

    private static ProviderConnectionDto MapProviderConnection(ProviderConnection entity)
        => new(
            entity.Id,
            entity.Name,
            entity.ProviderType,
            MaskApiKey(entity.ApiKey),
            entity.BaseUrl,
            entity.Endpoint,
            entity.ProviderName,
            entity.RelativePath,
            entity.ApiKeyHeaderName,
            entity.AuthMode,
            entity.ApiVersion,
            entity.IsEnabled,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static ModelDto MapModel(StudioModel entity)
        => new(
            entity.Id,
            entity.ProviderConnectionId,
            entity.ProviderConnection?.Name ?? string.Empty,
            entity.ProviderConnection?.ProviderType ?? ProviderType.OpenAI,
            entity.DisplayName,
            entity.ModelKey,
            entity.SupportsStreaming,
            entity.SupportsTools,
            entity.SupportsVision,
            entity.IsEnabled,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static void ValidateProviderConnection(ProviderConnection entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            throw new InvalidOperationException("Provider connection name is required.");
        }

        if (string.IsNullOrWhiteSpace(entity.ApiKey))
        {
            throw new InvalidOperationException("API key is required.");
        }

        if (entity.ProviderType == ProviderType.OpenAI && string.IsNullOrWhiteSpace(entity.BaseUrl) == false && !Uri.TryCreate(entity.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("OpenAI base URL must be a valid absolute URL.");
        }

        if (entity.ProviderType == ProviderType.OpenAICompatible)
        {
            if (!Uri.TryCreate(entity.BaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("OpenAI-compatible base URL is required.");
            }

            if (string.IsNullOrWhiteSpace(entity.ProviderName))
            {
                throw new InvalidOperationException("OpenAI-compatible provider name is required.");
            }
        }

        if (entity.ProviderType == ProviderType.DeepSeek)
        {
            if (!Uri.TryCreate(entity.BaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("DeepSeek base URL is required.");
            }
        }

        if (entity.ProviderType == ProviderType.AzureOpenAI && !Uri.TryCreate(entity.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Azure OpenAI endpoint is required.");
        }
    }

    private static void ValidateModel(StudioModel entity)
    {
        if (string.IsNullOrWhiteSpace(entity.DisplayName))
        {
            throw new InvalidOperationException("Model display name is required.");
        }

        if (string.IsNullOrWhiteSpace(entity.ModelKey))
        {
            throw new InvalidOperationException("Model key is required.");
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string DeepSeekProviderName = "deepseek";
    private const string DeepSeekBaseUrl = "https://api.deepseek.com";
    private const string DeepSeekRelativePath = "chat/completions";

    private static string NormalizeProviderName(string? value)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException("Provider name is required.") : value.Trim().ToLowerInvariant();

    private static OpenAICompatibleAuthMode ParseAuthMode(string? value)
        => Enum.TryParse<OpenAICompatibleAuthMode>(value, true, out var result) ? result : OpenAICompatibleAuthMode.Bearer;

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        if (apiKey.Length <= 8)
        {
            return new string('*', apiKey.Length);
        }

        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }
}
