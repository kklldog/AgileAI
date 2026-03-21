using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.Tests;

public class DefaultSkillContinuationPolicyTests
{
    [Fact]
    public async Task DecideAsync_WithActiveSkill_ShouldContinue()
    {
        var policy = new DefaultSkillContinuationPolicy();
        var skill = new LocalFileSkill(new SkillManifest { Name = "weather" }, new NoopSkillExecutor());

        var decision = await policy.DecideAsync(
            new AgentRequest { Input = "continue", EnableSkills = true },
            new ConversationState { SessionId = "s1", ActiveSkill = "weather" },
            [skill]);

        Assert.True(decision.ContinueActiveSkill);
        Assert.Equal("weather", decision.SkillName);
    }

    [Fact]
    public async Task DecideAsync_WithPreferredSkillSwitch_ShouldNotContinue()
    {
        var policy = new DefaultSkillContinuationPolicy();
        var skill = new LocalFileSkill(new SkillManifest { Name = "weather" }, new NoopSkillExecutor());

        var decision = await policy.DecideAsync(
            new AgentRequest { Input = "switch", EnableSkills = true, PreferredSkill = "calendar" },
            new ConversationState { SessionId = "s1", ActiveSkill = "weather" },
            [skill]);

        Assert.False(decision.ContinueActiveSkill);
    }

    [Fact]
    public async Task DecideAsync_WithExplicitExitPhrase_ShouldNotContinue()
    {
        var policy = new DefaultSkillContinuationPolicy();
        var skill = new LocalFileSkill(new SkillManifest { Name = "weather" }, new NoopSkillExecutor());

        var decision = await policy.DecideAsync(
            new AgentRequest { Input = "stop using the weather skill", EnableSkills = true },
            new ConversationState { SessionId = "s1", ActiveSkill = "weather" },
            [skill]);

        Assert.False(decision.ContinueActiveSkill);
        Assert.Contains("exit", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecideAsync_WithManifestExitRule_ShouldNotContinue()
    {
        var policy = new DefaultSkillContinuationPolicy();
        var skill = new LocalFileSkill(
            new SkillManifest
            {
                Name = "travel",
                Metadata = new Dictionary<string, string>
                {
                    ["exitOn"] = "done booking, itinerary complete"
                }
            },
            new NoopSkillExecutor());

        var decision = await policy.DecideAsync(
            new AgentRequest { Input = "done booking for now", EnableSkills = true },
            new ConversationState { SessionId = "s1", ActiveSkill = "travel" },
            [skill]);

        Assert.False(decision.ContinueActiveSkill);
        Assert.Contains("skill-specific", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecideAsync_WithManifestContinueRule_ShouldContinue()
    {
        var policy = new DefaultSkillContinuationPolicy();
        var skill = new LocalFileSkill(
            new SkillManifest
            {
                Name = "travel",
                Metadata = new Dictionary<string, string>
                {
                    ["continueOn"] = "keep planning, continue itinerary"
                }
            },
            new NoopSkillExecutor());

        var decision = await policy.DecideAsync(
            new AgentRequest { Input = "keep planning my trip", EnableSkills = true },
            new ConversationState { SessionId = "s1", ActiveSkill = "travel" },
            [skill]);

        Assert.True(decision.ContinueActiveSkill);
        Assert.Equal("travel", decision.SkillName);
        Assert.Contains("skill-specific", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecideAsync_WithStrongCompetingSkill_ShouldNotContinue()
    {
        var policy = new DefaultSkillContinuationPolicy();
        var weather = new LocalFileSkill(
            new SkillManifest { Name = "weather", Description = "Weather helper", Triggers = ["forecast", "temperature"] },
            new NoopSkillExecutor());
        var calendar = new LocalFileSkill(
            new SkillManifest { Name = "calendar", Description = "Calendar scheduling helper", Triggers = ["meeting", "schedule"] },
            new NoopSkillExecutor());

        var decision = await policy.DecideAsync(
            new AgentRequest { Input = "schedule a meeting on my calendar tomorrow", EnableSkills = true },
            new ConversationState { SessionId = "s1", ActiveSkill = "weather" },
            [weather, calendar]);

        Assert.False(decision.ContinueActiveSkill);
        Assert.Contains("calendar", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoopSkillExecutor : ISkillExecutor
    {
        public Task<AgentResult> ExecuteAsync(SkillManifest manifest, SkillExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResult { IsSuccess = true });
    }
}
