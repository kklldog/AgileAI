using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Extensions.FileSystem;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgileAI.Studio.Api.Services;

public class AgentService(StudioDbContext dbContext, ModelCatalogService modelCatalogService, StudioToolRegistryFactory toolRegistryFactory)
{
    public async Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        var agents = await dbContext.Agents
            .Include(x => x.StudioModel)
            .ThenInclude(x => x!.ProviderConnection)
            .ToListAsync(cancellationToken);

        agents = agents
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAtUtc.UtcDateTime)
            .ToList();

        var list = new List<AgentDto>(agents.Count);
        foreach (var item in agents)
        {
            list.Add(await MapAgentAsync(item, cancellationToken));
        }

        return list;
    }

    public async Task<AgentDto> CreateAgentAsync(AgentRequestDto request, CancellationToken cancellationToken)
    {
        var model = await dbContext.Models.Include(x => x.ProviderConnection).FirstOrDefaultAsync(x => x.Id == request.StudioModelId, cancellationToken)
            ?? throw new InvalidOperationException("Model not found.");

        var now = DateTimeOffset.UtcNow;
        var entity = new AgentDefinition
        {
            Id = Guid.NewGuid(),
            StudioModelId = model.Id,
            StudioModel = model,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            SystemPrompt = request.SystemPrompt.Trim(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            EnableSkills = request.EnableSkills,
            IsPinned = request.IsPinned,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ValidateAgent(entity);
        dbContext.Agents.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SaveSelectedToolsAsync(entity.Id, request.SelectedToolNames, cancellationToken);
        return await MapAgentAsync(entity, cancellationToken);
    }

    public async Task<AgentDto> UpdateAgentAsync(Guid id, AgentRequestDto request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Agents.Include(x => x.StudioModel).ThenInclude(x => x!.ProviderConnection).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Agent not found.");
        var model = await dbContext.Models.Include(x => x.ProviderConnection).FirstOrDefaultAsync(x => x.Id == request.StudioModelId, cancellationToken)
            ?? throw new InvalidOperationException("Model not found.");

        entity.StudioModelId = model.Id;
        entity.StudioModel = model;
        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.SystemPrompt = request.SystemPrompt.Trim();
        entity.Temperature = request.Temperature;
        entity.MaxTokens = request.MaxTokens;
        entity.EnableSkills = request.EnableSkills;
        entity.IsPinned = request.IsPinned;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        ValidateAgent(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SaveSelectedToolsAsync(entity.Id, request.SelectedToolNames, cancellationToken);
        return await MapAgentAsync(entity, cancellationToken);
    }

    public async Task DeleteAgentAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Agents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Agent not found.");

        dbContext.Agents.Remove(entity);
        var selection = await dbContext.AgentToolSelections.FirstOrDefaultAsync(x => x.AgentDefinitionId == id, cancellationToken);
        if (selection != null)
        {
            dbContext.AgentToolSelections.Remove(selection);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AgentDefinition> GetAgentEntityAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Agents.Include(x => x.StudioModel).ThenInclude(x => x!.ProviderConnection).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Agent not found.");

    public IReadOnlyList<ToolOptionDto> GetAvailableTools()
        => toolRegistryFactory.CreateDefaultRegistry()
            .GetToolDefinitions()
            .Select(x => new ToolOptionDto(x.Name, x.Description))
            .ToList();

    public async Task<IReadOnlyList<string>> GetSelectedToolNamesAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var selection = await dbContext.AgentToolSelections.FirstOrDefaultAsync(x => x.AgentDefinitionId == agentId, cancellationToken);
        return NormalizeSelectedTools(ParseToolNames(selection?.ToolNamesJson));
    }

    private async Task<AgentDto> MapAgentAsync(AgentDefinition entity, CancellationToken cancellationToken)
    {
        var runtime = await modelCatalogService.GetRuntimeOptionsAsync(entity.StudioModelId, cancellationToken);
        var selectedToolNames = await GetSelectedToolNamesAsync(entity.Id, cancellationToken);
        return new AgentDto(
            entity.Id,
            entity.StudioModelId,
            entity.Name,
            entity.Description,
            entity.SystemPrompt,
            entity.Temperature,
            entity.MaxTokens,
            entity.EnableSkills,
            entity.IsPinned,
            selectedToolNames,
            entity.StudioModel?.DisplayName ?? string.Empty,
            runtime.RuntimeModelId,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    private async Task SaveSelectedToolsAsync(Guid agentId, IReadOnlyList<string>? selectedToolNames, CancellationToken cancellationToken)
    {
        var normalized = NormalizeSelectedTools(selectedToolNames);
        var entity = await dbContext.AgentToolSelections.FirstOrDefaultAsync(x => x.AgentDefinitionId == agentId, cancellationToken);
        if (entity == null)
        {
            entity = new AgentToolSelection { AgentDefinitionId = agentId };
            dbContext.AgentToolSelections.Add(entity);
        }

        entity.ToolNamesJson = JsonSerializer.Serialize(normalized);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyList<string> NormalizeSelectedTools(IReadOnlyList<string>? selectedToolNames)
    {
        var available = toolRegistryFactory.CreateDefaultRegistry()
            .GetToolDefinitions()
            .Select(x => x.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (selectedToolNames == null || selectedToolNames.Count == 0)
        {
            return available;
        }

        return selectedToolNames
            .Where(name => available.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> ParseToolNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static void ValidateAgent(AgentDefinition entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            throw new InvalidOperationException("Agent name is required.");
        }

        if (string.IsNullOrWhiteSpace(entity.SystemPrompt))
        {
            throw new InvalidOperationException("System prompt is required.");
        }

        if (entity.MaxTokens <= 0)
        {
            throw new InvalidOperationException("Max tokens must be greater than zero.");
        }
    }
}
