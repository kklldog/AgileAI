using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgileAI.Tests;

public class StudioPromptSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutStudioModelId_ShouldThrow()
    {
        var services = new ServiceCollection();
        var executor = new StudioPromptSkillExecutor(services.BuildServiceProvider());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            new SkillManifest { Name = "planner", InstructionBody = "Help." },
            new SkillExecutionContext
            {
                Request = new AgentRequest { Input = "hello", ModelId = "ignored" },
                ModelId = "runtime-model"
            }));

        Assert.Equal("studioModelId is required for Studio skill execution.", error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithStudioModelId_ShouldResolveServicesAndForwardToPromptExecutor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StudioDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new StudioDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var modelId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var provider = new ProviderConnection
        {
            Id = Guid.NewGuid(),
            Name = "Mock Provider",
            ProviderType = ProviderType.OpenAI,
            ApiKey = "demo-local",
            BaseUrl = "mock://studio/v1/",
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var model = new StudioModel
        {
            Id = modelId,
            ProviderConnection = provider,
            ProviderConnectionId = provider.Id,
            DisplayName = "Mock Model",
            ModelKey = "gpt-4o-mini",
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.ProviderConnections.Add(provider);
        dbContext.Models.Add(model);
        await dbContext.SaveChangesAsync();

        var toolRegistry = new InMemoryToolRegistry();
        toolRegistry.Register(new TestTool());

        var services = new ServiceCollection();
        services.AddSingleton(new ModelCatalogService(dbContext, new ProviderClientFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)));
        services.AddSingleton(new ProviderClientFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));
        var executor = new StudioPromptSkillExecutor(services.BuildServiceProvider(), toolRegistry);

        var result = await executor.ExecuteAsync(
            new SkillManifest { Name = "planner", InstructionBody = "You are helpful." },
            new SkillExecutionContext
            {
                Request = new AgentRequest { Input = "hello", ModelId = "ignored" },
                ModelId = "skill-model",
                Items = new Dictionary<string, object?>
                {
                    ["studioModelId"] = modelId.ToString()
                }
            });

        Assert.True(result.IsSuccess);
        Assert.Contains("Mock response from AgileAI Studio for: hello", result.Output);
        Assert.NotNull(result.UpdatedHistory);
        Assert.Contains(result.UpdatedHistory!, message => message.Role == ChatRole.User && message.TextContent == "hello");
        Assert.Contains(result.UpdatedHistory!, message => message.Role == ChatRole.Assistant && message.TextContent?.Contains("Mock response from AgileAI Studio") == true);
    }

    private sealed class TestTool : ITool
    {
        public string Name => "test_tool";
        public string? Description => "test";
        public object? ParametersSchema => new { type = "object", properties = new { } };

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResult { ToolCallId = context.ToolCall.Id, Content = "ok" });
    }
}
