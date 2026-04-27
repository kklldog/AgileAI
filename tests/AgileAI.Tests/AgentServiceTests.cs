using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgileAI.Tests;

public class AgentServiceTests
{
    [Fact]
    public async Task GetSelectedToolNamesAsync_WithoutSelection_ShouldReturnAllAvailableTools()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext, new InMemorySkillRegistry());
        var agentId = Guid.NewGuid();

        var selected = await service.GetSelectedToolNamesAsync(agentId, CancellationToken.None);
        var available = service.GetAvailableTools().Select(x => x.Name).ToList();

        Assert.NotEmpty(selected);
        Assert.Equal(available, selected);
    }

    [Fact]
    public async Task GetAllowedSkillNamesAsync_WithoutSelection_ShouldReturnAllLoadedSkills()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateSkill("alpha").Object);
        registry.Register(CreateSkill("beta").Object);
        var service = CreateService(dbContext, registry);

        var selected = await service.GetAllowedSkillNamesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["alpha", "beta"], selected);
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldNormalizeAndPersistValidToolsAndSkills()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateSkill("planner").Object);
        registry.Register(CreateSkill("writer").Object);
        var service = CreateService(dbContext, registry);
        var model = await SeedModelAsync(dbContext);

        var created = await service.CreateAgentAsync(
            new AgentRequestDto(
                model.Id,
                "  Studio Agent  ",
                "  Helpful assistant  ",
                "  System prompt  ",
                0.4,
                512,
                "medium",
                true,
                true,
                ["read_file", "invalid-tool", "read_file", "run_local_command"],
                ["writer", "missing", "writer", "planner"]),
            CancellationToken.None);

        Assert.Equal("Studio Agent", created.Name);
        Assert.Equal("Helpful assistant", created.Description);
        Assert.Equal("System prompt", created.SystemPrompt);
        Assert.Equal(["read_file", "run_local_command"], created.SelectedToolNames);
        Assert.Equal(["writer", "planner"], created.AllowedSkillNames);
        Assert.Equal($"openai:{model.ModelKey}", created.RuntimeModelId);

        var toolSelection = await dbContext.AgentToolSelections.SingleAsync();
        var skillSelection = await dbContext.AgentSkillSelections.SingleAsync();
        Assert.Contains("read_file", toolSelection.ToolNamesJson);
        Assert.DoesNotContain("invalid-tool", toolSelection.ToolNamesJson);
        Assert.Contains("writer", skillSelection.SkillNamesJson);
        Assert.DoesNotContain("missing", skillSelection.SkillNamesJson);
    }

    [Fact]
    public async Task UpdateAgentAsync_ShouldReplaceSelectionsAndPinnedState()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateSkill("planner").Object);
        registry.Register(CreateSkill("writer").Object);
        var service = CreateService(dbContext, registry);
        var model = await SeedModelAsync(dbContext);
        var created = await service.CreateAgentAsync(
            new AgentRequestDto(model.Id, "Agent", "Desc", "Prompt", 0.2, 128, null, false, false, ["read_file"], ["planner"]),
            CancellationToken.None);

        var updated = await service.UpdateAgentAsync(
            created.Id,
            new AgentRequestDto(model.Id, "Updated", "Updated desc", "Updated prompt", 0.9, 256, "high", true, true, ["write_file", "write_file"], ["writer", "writer"]),
            CancellationToken.None);

        Assert.Equal("Updated", updated.Name);
        Assert.Equal("Updated desc", updated.Description);
        Assert.Equal("Updated prompt", updated.SystemPrompt);
        Assert.True(updated.IsPinned);
        Assert.True(updated.EnableSkills);
        Assert.Equal(["write_file"], updated.SelectedToolNames);
        Assert.Equal(["writer"], updated.AllowedSkillNames);
    }

    [Fact]
    public async Task CreateAgentAsync_WithEmptyToolSelection_ShouldPersistNoTools()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext, new InMemorySkillRegistry());
        var model = await SeedModelAsync(dbContext);

        var created = await service.CreateAgentAsync(
            new AgentRequestDto(model.Id, "Agent", "Desc", "Prompt", 0.2, 128, null, false, false, [], []),
            CancellationToken.None);

        Assert.Empty(created.SelectedToolNames);

        var toolSelection = await dbContext.AgentToolSelections.SingleAsync();
        Assert.Equal("[]", toolSelection.ToolNamesJson);
    }

    [Fact]
    public async Task UpdateAgentAsync_WithEmptyToolSelection_ShouldClearTools()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext, new InMemorySkillRegistry());
        var model = await SeedModelAsync(dbContext);

        var created = await service.CreateAgentAsync(
            new AgentRequestDto(model.Id, "Agent", "Desc", "Prompt", 0.2, 128, null, false, false, ["read_file"], []),
            CancellationToken.None);

        var updated = await service.UpdateAgentAsync(
            created.Id,
            new AgentRequestDto(model.Id, "Agent", "Desc", "Prompt", 0.2, 128, null, false, false, [], []),
            CancellationToken.None);

        Assert.Empty(updated.SelectedToolNames);

        var toolSelection = await dbContext.AgentToolSelections.SingleAsync();
        Assert.Equal("[]", toolSelection.ToolNamesJson);
    }

    [Fact]
    public async Task DeleteAgentAsync_ShouldRemoveSelections()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateSkill("planner").Object);
        var service = CreateService(dbContext, registry);
        var model = await SeedModelAsync(dbContext);
        var created = await service.CreateAgentAsync(
            new AgentRequestDto(model.Id, "Agent", "Desc", "Prompt", 0.2, 128, null, true, false, ["read_file"], ["planner"]),
            CancellationToken.None);

        await service.DeleteAgentAsync(created.Id, CancellationToken.None);

        Assert.Empty(await dbContext.Agents.ToListAsync());
        Assert.Empty(await dbContext.AgentToolSelections.ToListAsync());
        Assert.Empty(await dbContext.AgentSkillSelections.ToListAsync());
    }

    [Fact]
    public async Task CreateAgentAsync_WithInvalidRequest_ShouldThrow()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext, new InMemorySkillRegistry());
        var model = await SeedModelAsync(dbContext);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAgentAsync(
            new AgentRequestDto(model.Id, "   ", "Desc", "Prompt", 0.2, 0, null, false, false, null, null),
            CancellationToken.None));

        Assert.Equal("Agent name is required.", error.Message);
    }

    private static AgentService CreateService(StudioDbContext dbContext, ISkillRegistry skillRegistry)
    {
        var modelCatalogService = new ModelCatalogService(dbContext, new ProviderClientFactory(NullLoggerFactory.Instance));
        var fileSystemOptions = new FileSystemToolOptions
        {
            RootPath = Path.GetTempPath(),
            MaxReadCharacters = 12000
        };
        var pathGuard = new FileSystemPathGuard(fileSystemOptions);
        var webFetchHttpClient = new HttpClient(new FakeHttpMessageHandler((request, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            })));
        var fileSystemFactory = new FileSystemToolRegistryFactory(
            new ListDirectoryTool(pathGuard),
            new SearchFilesTool(pathGuard),
            new ReadFileTool(pathGuard, fileSystemOptions),
            new ReadFilesBatchTool(pathGuard, fileSystemOptions),
            new WriteFileTool(pathGuard),
            new CreateDirectoryTool(pathGuard),
            new MoveFileTool(pathGuard),
            new PatchFileTool(pathGuard),
            new DeleteFileTool(pathGuard, fileSystemOptions),
            new DeleteDirectoryTool(pathGuard));
        var studioRegistryFactory = new StudioToolRegistryFactory(
            fileSystemFactory,
            new RunLocalCommandTool(new AgileAI.Core.ProcessExecutionService()),
            new WebFetchTool(webFetchHttpClient));
        return new AgentService(dbContext, modelCatalogService, studioRegistryFactory, skillRegistry);
    }

    private static Mock<ISkill> CreateSkill(string name)
    {
        var skill = new Mock<ISkill>();
        skill.SetupGet(x => x.Name).Returns(name);
        skill.SetupGet(x => x.Description).Returns($"{name} description");
        skill.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = name, Description = $"{name} description" });
        return skill;
    }

    private static async Task<StudioModel> SeedModelAsync(StudioDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new ProviderConnection
        {
            Id = Guid.NewGuid(),
            Name = "Provider",
            ProviderType = ProviderType.OpenAI,
            ApiKey = "demo-local",
            BaseUrl = "mock://studio/v1/",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var model = new StudioModel
        {
            Id = Guid.NewGuid(),
            ProviderConnection = provider,
            ProviderConnectionId = provider.Id,
            DisplayName = "Demo Model",
            ModelKey = "gpt-4o-mini",
            SupportsStreaming = true,
            SupportsTools = true,
            ThinkingIntensitiesJson = "[\"low\",\"medium\",\"high\"]",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.ProviderConnections.Add(provider);
        dbContext.Models.Add(model);
        await dbContext.SaveChangesAsync();
        return model;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<StudioDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<StudioDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new StudioDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }
}
