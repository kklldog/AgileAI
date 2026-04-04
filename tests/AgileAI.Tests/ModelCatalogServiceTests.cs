using AgileAI.Abstractions;
using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgileAI.Tests;

public class ModelCatalogServiceTests
{
    [Fact]
    public async Task CreateProviderConnectionAsync_WithOpenAICompatibleRequest_ShouldTrimPersistAndMaskApiKey()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);

        var created = await service.CreateProviderConnectionAsync(
            new ProviderConnectionRequest(
                "  OpenRouter  ",
                ProviderType.OpenAICompatible,
                "  sk-test-12345678  ",
                "  https://openrouter.ai/api/v1/  ",
                null,
                "  OpenRouter  ",
                "  chat/completions  ",
                "  x-api-key  ",
                "  Header  ",
                null,
                true),
            CancellationToken.None);

        Assert.Equal("OpenRouter", created.Name);
        Assert.Equal("sk-t...5678", created.ApiKeyPreview);
        Assert.Equal("https://openrouter.ai/api/v1/", created.BaseUrl);
        Assert.Equal("OpenRouter", created.ProviderName);
        Assert.Equal("chat/completions", created.RelativePath);
        Assert.Equal("x-api-key", created.ApiKeyHeaderName);
        Assert.Equal("Header", created.AuthMode);

        var entity = await dbContext.ProviderConnections.SingleAsync();
        Assert.Equal("sk-test-12345678", entity.ApiKey);
        Assert.Equal("OpenRouter", entity.Name);
    }

    [Fact]
    public async Task CreateProviderConnectionAsync_WithInvalidOpenAICompatibleBaseUrl_ShouldThrow()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateProviderConnectionAsync(
            new ProviderConnectionRequest(
                "Provider",
                ProviderType.OpenAICompatible,
                "sk-test",
                "not-a-url",
                null,
                "gateway",
                null,
                null,
                null,
                null,
                true),
            CancellationToken.None));

        Assert.Equal("OpenAI-compatible base URL is required.", error.Message);
    }

    [Fact]
    public async Task CreateProviderConnectionAsync_WithMissingOpenAICompatibleProviderName_ShouldThrow()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateProviderConnectionAsync(
            new ProviderConnectionRequest(
                "Provider",
                ProviderType.OpenAICompatible,
                "sk-test",
                "https://gateway.example/v1/",
                null,
                "   ",
                null,
                null,
                null,
                null,
                true),
            CancellationToken.None));

        Assert.Equal("OpenAI-compatible provider name is required.", error.Message);
    }

    [Fact]
    public async Task CreateProviderConnectionAsync_WithInvalidAzureEndpoint_ShouldThrow()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateProviderConnectionAsync(
            new ProviderConnectionRequest(
                "Azure",
                ProviderType.AzureOpenAI,
                "azure-key",
                null,
                "nope",
                null,
                null,
                null,
                null,
                "2024-02-01",
                true),
            CancellationToken.None));

        Assert.Equal("Azure OpenAI endpoint is required.", error.Message);
    }

    [Fact]
    public async Task CreateModelAsync_AndGetRuntimeOptionsAsync_ShouldNormalizeProviderNameAndFallbackAuthMode()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);

        var provider = await service.CreateProviderConnectionAsync(
            new ProviderConnectionRequest(
                "Gateway",
                ProviderType.OpenAICompatible,
                "sk-gateway-12345678",
                "https://gateway.example/v1/",
                null,
                "  My Gateway  ",
                null,
                null,
                "not-a-real-mode",
                null,
                true),
            CancellationToken.None);

        var model = await service.CreateModelAsync(
            new ModelRequest(
                provider.Id,
                "  Fast Model  ",
                "  demo-model  ",
                true,
                true,
                false,
                true),
            CancellationToken.None);

        var runtime = await service.GetRuntimeOptionsAsync(model.Id, CancellationToken.None);

        Assert.Equal("my gateway", runtime.RuntimeProviderName);
        Assert.Equal("my gateway:demo-model", runtime.RuntimeModelId);
        Assert.Equal(AgileAI.Providers.OpenAICompatible.OpenAICompatibleAuthMode.Bearer, runtime.AuthMode);
    }

    [Fact]
    public async Task TestModelAsync_WithMockProvider_ShouldReturnSuccessMessage()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);

        var provider = await service.CreateProviderConnectionAsync(
            new ProviderConnectionRequest(
                "Demo OpenAI",
                ProviderType.OpenAI,
                "demo-local",
                "mock://studio/v1/",
                null,
                null,
                null,
                null,
                null,
                null,
                true),
            CancellationToken.None);

        var model = await service.CreateModelAsync(
            new ModelRequest(provider.Id, "Demo", "gpt-4o-mini", true, true, false, true),
            CancellationToken.None);

        var result = await service.TestModelAsync(model.Id, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Connection succeeded.", result.Message);
        Assert.Contains("OK", result.Message);
    }

    [Fact]
    public async Task DeleteProviderConnectionAsync_ShouldRemoveDependentModelsAgentsConversationsAndMessages()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);
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
            DisplayName = "Demo",
            ModelKey = "gpt-4o-mini",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var agent = new AgentDefinition
        {
            Id = Guid.NewGuid(),
            StudioModel = model,
            StudioModelId = model.Id,
            Name = "Agent",
            Description = "desc",
            SystemPrompt = "prompt",
            MaxTokens = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            AgentDefinition = agent,
            AgentDefinitionId = agent.Id,
            Title = "Conversation",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            Conversation = conversation,
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = "hello",
            CreatedAtUtc = now
        };

        provider.Models.Add(model);
        model.Agents.Add(agent);
        agent.Conversations.Add(conversation);
        conversation.Messages.Add(message);

        dbContext.ProviderConnections.Add(provider);
        await dbContext.SaveChangesAsync();

        await service.DeleteProviderConnectionAsync(provider.Id, CancellationToken.None);

        Assert.Empty(await dbContext.ProviderConnections.ToListAsync());
        Assert.Empty(await dbContext.Models.ToListAsync());
        Assert.Empty(await dbContext.Agents.ToListAsync());
        Assert.Empty(await dbContext.Conversations.ToListAsync());
        Assert.Empty(await dbContext.Messages.ToListAsync());
    }

    [Fact]
    public async Task DeleteModelAsync_ShouldRemoveDependentAgentsConversationsAndMessages()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = CreateService(dbContext);
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
            DisplayName = "Demo",
            ModelKey = "gpt-4o-mini",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var agent = new AgentDefinition
        {
            Id = Guid.NewGuid(),
            StudioModel = model,
            StudioModelId = model.Id,
            Name = "Agent",
            Description = "desc",
            SystemPrompt = "prompt",
            MaxTokens = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            AgentDefinition = agent,
            AgentDefinitionId = agent.Id,
            Title = "Conversation",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            Conversation = conversation,
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = "hello",
            CreatedAtUtc = now
        };

        provider.Models.Add(model);
        model.Agents.Add(agent);
        agent.Conversations.Add(conversation);
        conversation.Messages.Add(message);

        dbContext.ProviderConnections.Add(provider);
        await dbContext.SaveChangesAsync();

        await service.DeleteModelAsync(model.Id, CancellationToken.None);

        Assert.Single(await dbContext.ProviderConnections.ToListAsync());
        Assert.Empty(await dbContext.Models.ToListAsync());
        Assert.Empty(await dbContext.Agents.ToListAsync());
        Assert.Empty(await dbContext.Conversations.ToListAsync());
        Assert.Empty(await dbContext.Messages.ToListAsync());
    }

    private static ModelCatalogService CreateService(StudioDbContext dbContext)
        => new(dbContext, new ProviderClientFactory(NullLoggerFactory.Instance));

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
