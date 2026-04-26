using AgileAI.Studio.Api.Domain;

namespace AgileAI.Studio.Api.Contracts;

public record ProviderConnectionRequest(
    string Name,
    ProviderType ProviderType,
    string ApiKey,
    string? BaseUrl,
    string? Endpoint,
    string? ProviderName,
    string? RelativePath,
    string? ApiKeyHeaderName,
    string? AuthMode,
    string? ApiVersion,
    bool IsEnabled);

public record ModelRequest(
    Guid ProviderConnectionId,
    string DisplayName,
    string ModelKey,
    bool SupportsStreaming,
    bool SupportsTools,
    bool SupportsVision,
    IReadOnlyList<string>? ThinkingIntensities,
    bool IsEnabled);

public record AgentRequestDto(
    Guid StudioModelId,
    string Name,
    string Description,
    string SystemPrompt,
    double Temperature,
    int MaxTokens,
    string? ThinkingIntensity,
    bool EnableSkills,
    bool IsPinned,
    IReadOnlyList<string>? SelectedToolNames,
    IReadOnlyList<string>? AllowedSkillNames);

public record ToolOptionDto(
    string Name,
    string? Description);

public record ConversationCreateRequest(Guid AgentId, string? Title);

public record SendMessageRequest(string Content);

public record ProviderConnectionDto(
    Guid Id,
    string Name,
    ProviderType ProviderType,
    string ApiKeyPreview,
    string? BaseUrl,
    string? Endpoint,
    string? ProviderName,
    string? RelativePath,
    string? ApiKeyHeaderName,
    string? AuthMode,
    string? ApiVersion,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public record ModelDto(
    Guid Id,
    Guid ProviderConnectionId,
    string ProviderConnectionName,
    ProviderType ProviderType,
    string DisplayName,
    string ModelKey,
    bool SupportsStreaming,
    bool SupportsTools,
    bool SupportsVision,
    IReadOnlyList<string> ThinkingIntensities,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public record AgentDto(
    Guid Id,
    Guid StudioModelId,
    string Name,
    string Description,
    string SystemPrompt,
    double Temperature,
    int MaxTokens,
    string? ThinkingIntensity,
    bool EnableSkills,
    bool IsPinned,
    IReadOnlyList<string> SelectedToolNames,
    IReadOnlyList<string> AllowedSkillNames,
    string ModelDisplayName,
    string RuntimeModelId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public record SkillDto(
    string Name,
    string? Description,
    string? Version,
    string? EntryMode,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Files);

public record ConversationSkillStateDto(
    string Name,
    string? Description);

public record ConversationDto(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int MessageCount,
    ConversationSkillStateDto? ActiveSkill);

public record MessageDto(
    Guid Id,
    Guid ConversationId,
    MessageRole Role,
    string Content,
    bool IsStreaming,
    string? FinishReason,
    int? InputTokens,
    int? OutputTokens,
    string? AppliedSkillName,
    IReadOnlyList<string>? AppliedToolNames,
    DateTimeOffset CreatedAtUtc);

public record ChatResultDto(
    ConversationDto Conversation,
    MessageDto UserMessage,
    MessageDto AssistantMessage);

public record ConnectionTestResultDto(bool Success, string Message);

public record StudioOverviewDto(int ModelCount, int AgentCount, int ConversationCount, IReadOnlyList<ConversationDto> RecentConversations);
