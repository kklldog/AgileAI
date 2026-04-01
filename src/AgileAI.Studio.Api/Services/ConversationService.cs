using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgileAI.Studio.Api.Services;

public class ConversationService(StudioDbContext dbContext, SkillService skillService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ConversationDto>> GetConversationsAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.Conversations
            .Include(x => x.AgentDefinition)
            .Include(x => x.Messages)
            .ToListAsync(cancellationToken);

        var ordered = items.OrderByDescending(x => x.UpdatedAtUtc.UtcDateTime).ToList();
        var mapped = new List<ConversationDto>(ordered.Count);
        foreach (var item in ordered)
        {
            mapped.Add(await MapConversationAsync(item, cancellationToken));
        }

        return mapped;
    }

    public async Task<ConversationDto> CreateConversationAsync(ConversationCreateRequest request, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents.FirstOrDefaultAsync(x => x.Id == request.AgentId, cancellationToken)
            ?? throw new InvalidOperationException("Agent not found.");

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            AgentDefinitionId = agent.Id,
            AgentDefinition = agent,
            Title = string.IsNullOrWhiteSpace(request.Title) ? $"{agent.Name} chat" : request.Title.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MapConversationAsync(conversation, cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var items = await dbContext.Messages
            .Where(x => x.ConversationId == conversationId)
            .ToListAsync(cancellationToken);

        return items
            .OrderBy(x => x.CreatedAtUtc.UtcDateTime)
            .Select(MapMessage)
            .ToList();
    }

    public async Task<Conversation> GetConversationEntityAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Conversations
            .Include(x => x.AgentDefinition)
            .ThenInclude(x => x!.StudioModel)
            .ThenInclude(x => x!.ProviderConnection)
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Conversation not found.");

    public async Task<StudioOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var recentConversations = await dbContext.Conversations
            .Include(x => x.AgentDefinition)
            .Include(x => x.Messages)
            .ToListAsync(cancellationToken);

        var modelCount = await dbContext.Models.CountAsync(cancellationToken);
        var agentCount = await dbContext.Agents.CountAsync(cancellationToken);
        var conversationCount = await dbContext.Conversations.CountAsync(cancellationToken);

        var recentConversationDtos = new List<ConversationDto>();
        foreach (var conversation in recentConversations
                     .OrderByDescending(x => x.UpdatedAtUtc.UtcDateTime)
                     .Take(4))
        {
            recentConversationDtos.Add(await MapConversationAsync(conversation, cancellationToken));
        }

        return new StudioOverviewDto(
            modelCount,
            agentCount,
            conversationCount,
            recentConversationDtos);
    }

    public async Task TouchConversationAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        conversation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RenameConversationAsync(Conversation conversation, string title, CancellationToken cancellationToken)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return;
        }

        conversation.Title = trimmedTitle.Length > 120 ? trimmedTitle[..120].Trim() : trimmedTitle;
        conversation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConversationMessage> AddMessageAsync(Guid conversationId, MessageRole role, string content, bool isStreaming, string? finishReason, int? inputTokens, int? outputTokens, string? appliedSkillName, IReadOnlyList<string>? appliedToolNames, CancellationToken cancellationToken)
    {
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            IsStreaming = isStreaming,
            FinishReason = finishReason,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            AppliedSkillName = appliedSkillName,
            AppliedToolNamesJson = SerializeAppliedToolNames(appliedToolNames),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task UpdateMessageAsync(ConversationMessage message, string content, bool isStreaming, string? finishReason, int? inputTokens, int? outputTokens, string? appliedSkillName, IReadOnlyList<string>? appliedToolNames, CancellationToken cancellationToken)
    {
        message.Content = content;
        message.IsStreaming = isStreaming;
        message.FinishReason = finishReason;
        message.InputTokens = inputTokens;
        message.OutputTokens = outputTokens;
        message.AppliedSkillName = appliedSkillName;
        message.AppliedToolNamesJson = SerializeAppliedToolNames(appliedToolNames);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConversationDto> MapConversationAsync(Conversation entity, CancellationToken cancellationToken)
        => new(
            entity.Id,
            entity.AgentDefinitionId,
            entity.AgentDefinition?.Name ?? string.Empty,
            entity.Title,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.Messages.Count,
            await skillService.GetConversationSkillStateAsync(entity.Id, cancellationToken));

    public ConversationDto MapConversation(Conversation entity)
        => MapConversationAsync(entity, CancellationToken.None).GetAwaiter().GetResult();

    public static MessageDto MapMessage(ConversationMessage entity)
        => new(
            entity.Id,
            entity.ConversationId,
            entity.Role,
            entity.Content,
            entity.IsStreaming,
            entity.FinishReason,
            entity.InputTokens,
            entity.OutputTokens,
            entity.AppliedSkillName,
            DeserializeAppliedToolNames(entity.AppliedToolNamesJson),
            entity.CreatedAtUtc);

    private static string? SerializeAppliedToolNames(IReadOnlyList<string>? appliedToolNames)
    {
        if (appliedToolNames == null || appliedToolNames.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(appliedToolNames.Where(name => string.IsNullOrWhiteSpace(name) == false).Distinct(StringComparer.OrdinalIgnoreCase), JsonOptions);
    }

    private static IReadOnlyList<string>? DeserializeAppliedToolNames(string? appliedToolNamesJson)
    {
        if (string.IsNullOrWhiteSpace(appliedToolNamesJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<string>>(appliedToolNamesJson, JsonOptions);
    }
}
