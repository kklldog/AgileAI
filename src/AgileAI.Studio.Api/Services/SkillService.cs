using AgileAI.Abstractions;
using AgileAI.Studio.Api.Contracts;

namespace AgileAI.Studio.Api.Services;

public class SkillService(ISkillRegistry skillRegistry, ISessionStore sessionStore)
{
    public IReadOnlyList<SkillDto> GetLoadedSkills()
        => skillRegistry.GetAllSkills()
            .Select(skill => new SkillDto(
                skill.Name,
                skill.Manifest?.Description,
                skill.Manifest?.Version,
                skill.Manifest?.EntryMode,
                skill.Manifest?.Triggers ?? [],
                skill.Manifest?.Files ?? []))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<ConversationSkillStateDto?> GetConversationSkillStateAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var state = await sessionStore.GetAsync(conversationId.ToString(), cancellationToken);
        if (state == null || string.IsNullOrWhiteSpace(state.ActiveSkill))
        {
            return null;
        }

        if (skillRegistry.TryGetSkill(state.ActiveSkill, out var skill) && skill != null)
        {
            return new ConversationSkillStateDto(skill.Name, skill.Manifest?.Description);
        }

        return new ConversationSkillStateDto(state.ActiveSkill, null);
    }
}
