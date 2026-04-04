using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Studio.Api.Services;
using Moq;

namespace AgileAI.Tests;

public class SkillServiceTests
{
    [Fact]
    public void GetLoadedSkills_ShouldMapManifestAndSortCaseInsensitive()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateSkill("zeta", "Zeta description", "2.0.0", "manual", ["z"], ["README.md"]).Object);
        registry.Register(CreateSkill("Alpha", "Alpha description", "1.0.0", "auto", ["a"], ["skill.md"]).Object);
        var service = new SkillService(registry, new InMemorySessionStore());

        var skills = service.GetLoadedSkills();

        Assert.Equal(["Alpha", "zeta"], skills.Select(x => x.Name).ToList());
        Assert.Equal("Alpha description", skills[0].Description);
        Assert.Equal("1.0.0", skills[0].Version);
        Assert.Equal("auto", skills[0].EntryMode);
        Assert.Equal(["a"], skills[0].Triggers);
        Assert.Equal(["skill.md"], skills[0].Files);
    }

    [Fact]
    public async Task GetConversationSkillStateAsync_WithoutStoredState_ShouldReturnNull()
    {
        var service = new SkillService(new InMemorySkillRegistry(), new InMemorySessionStore());

        var result = await service.GetConversationSkillStateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetConversationSkillStateAsync_WithLoadedSkill_ShouldReturnDescription()
    {
        var sessionStore = new InMemorySessionStore();
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateSkill("planner", "Plans work", "1.0.0", "auto", ["plan"], ["prompt.md"]).Object);
        var conversationId = Guid.NewGuid();
        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = conversationId.ToString(),
            ActiveSkill = "planner",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var service = new SkillService(registry, sessionStore);

        var result = await service.GetConversationSkillStateAsync(conversationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("planner", result!.Name);
        Assert.Equal("Plans work", result.Description);
    }

    [Fact]
    public async Task GetConversationSkillStateAsync_WithUnknownSkill_ShouldReturnNameAndNullDescription()
    {
        var sessionStore = new InMemorySessionStore();
        var conversationId = Guid.NewGuid();
        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = conversationId.ToString(),
            ActiveSkill = "missing-skill",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var service = new SkillService(new InMemorySkillRegistry(), sessionStore);

        var result = await service.GetConversationSkillStateAsync(conversationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("missing-skill", result!.Name);
        Assert.Null(result.Description);
    }

    private static Mock<ISkill> CreateSkill(
        string name,
        string description,
        string version,
        string entryMode,
        IReadOnlyList<string> triggers,
        IReadOnlyList<string> files)
    {
        var skill = new Mock<ISkill>();
        skill.SetupGet(x => x.Name).Returns(name);
        skill.SetupGet(x => x.Description).Returns(description);
        skill.SetupGet(x => x.Manifest).Returns(new SkillManifest
        {
            Name = name,
            Description = description,
            Version = version,
            EntryMode = entryMode,
            Triggers = triggers,
            Files = files
        });
        return skill;
    }
}
