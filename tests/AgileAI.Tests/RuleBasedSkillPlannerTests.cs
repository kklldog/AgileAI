using AgileAI.Abstractions;
using AgileAI.Core;
using Moq;

namespace AgileAI.Tests;

public class RuleBasedSkillPlannerTests
{
    [Fact]
    public async Task PlanAsync_ShouldSelectBestMatchingSkill()
    {
        var planner = new RuleBasedSkillPlanner();
        var weather = new Mock<ISkill>();
        weather.SetupGet(x => x.Name).Returns("weather");
        weather.SetupGet(x => x.Description).Returns("Get weather and forecast information");
        weather.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "weather", Triggers = ["weather", "forecast"] });

        var code = new Mock<ISkill>();
        code.SetupGet(x => x.Name).Returns("code-review");
        code.SetupGet(x => x.Description).Returns("Review code diffs");
        code.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "code-review", Triggers = ["review", "diff"] });

        var plan = await planner.PlanAsync(new AgentRequest { Input = "please check the weather forecast for beijing" }, [weather.Object, code.Object]);

        Assert.True(plan.ShouldUseSkill);
        Assert.Equal("weather", plan.SkillName);
    }

    [Fact]
    public async Task PlanAsync_ShouldRespectAllowedSkillsFilter()
    {
        var planner = new RuleBasedSkillPlanner();
        var weather = new Mock<ISkill>();
        weather.SetupGet(x => x.Name).Returns("weather");
        weather.SetupGet(x => x.Description).Returns("Get weather and forecast information");
        weather.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "weather", Triggers = ["weather", "forecast"] });

        var code = new Mock<ISkill>();
        code.SetupGet(x => x.Name).Returns("code-review");
        code.SetupGet(x => x.Description).Returns("Review code diffs");
        code.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "code-review", Triggers = ["review", "diff"] });

        var plan = await planner.PlanAsync(new AgentRequest
        {
            Input = "please check the weather forecast for beijing",
            AllowedSkills = ["code-review"]
        }, [weather.Object, code.Object]);

        Assert.False(plan.ShouldUseSkill);
        Assert.Null(plan.SkillName);
    }
}
