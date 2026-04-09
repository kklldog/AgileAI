using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgileAI.Tests;

public class ToolApprovalServiceTests
{
    [Fact]
    public async Task ResolveApprovalAsync_Deny_ShouldPersistDecisionAndReturnUpdatedAssistantMessage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudioDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new StudioDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

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
            Id = Guid.NewGuid(),
            ProviderConnection = provider,
            ProviderConnectionId = provider.Id,
            DisplayName = "Mock Model",
            ModelKey = "gpt-4o-mini",
            SupportsStreaming = true,
            SupportsTools = true,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var agent = new AgentDefinition
        {
            Id = Guid.NewGuid(),
            StudioModel = model,
            StudioModelId = model.Id,
            Name = "Approver",
            Description = "approval test agent",
            SystemPrompt = "You are a test agent.",
            Temperature = 0.2,
            MaxTokens = 256,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            AgentDefinition = agent,
            AgentDefinitionId = agent.Id,
            Title = "Approval Test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var userMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            Conversation = conversation,
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = "Please run a command.",
            CreatedAtUtc = now
        };
        var assistantMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            Conversation = conversation,
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = "Command approval required for run_local_command.",
            CreatedAtUtc = now
        };

        dbContext.ProviderConnections.Add(provider);
        dbContext.Models.Add(model);
        dbContext.Agents.Add(agent);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(userMessage, assistantMessage);
        await dbContext.SaveChangesAsync();

        var modelCatalogService = new ModelCatalogService(dbContext, new ProviderClientFactory(NullLoggerFactory.Instance));
        var skillRegistry = new InMemorySkillRegistry();
        var sessionStore = new InMemorySessionStore();
        var skillService = new SkillService(skillRegistry, sessionStore);
        var conversationService = new ConversationService(dbContext, skillService);
        var fileSystemOptions = new FileSystemToolOptions
        {
            RootPath = "/tmp",
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
        var processExecutionService = new AgileAI.Core.ProcessExecutionService();
        var studioRegistryFactory = new StudioToolRegistryFactory(
            fileSystemFactory,
            new RunLocalCommandTool(processExecutionService),
            new WebFetchTool(webFetchHttpClient));
        var agentService = new AgentService(dbContext, modelCatalogService, studioRegistryFactory, skillRegistry);
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var streamingTurnFinalizer = new StudioStreamingTurnFinalizer(conversationService);
        var toolApprovalService = new ToolApprovalService(
            dbContext,
            conversationService,
            agentService,
            modelCatalogService,
            new ProviderClientFactory(NullLoggerFactory.Instance),
            serviceProvider,
            studioRegistryFactory,
            streamingTurnFinalizer);

        var created = await toolApprovalService.CreatePendingApprovalAsync(
            conversation,
            assistantMessage.Id,
            new AgileAI.Abstractions.ToolApprovalRequest
            {
                ToolCallId = "tool-call-1",
                ToolName = "run_local_command",
                Arguments = "{\"command\":\"echo hi\"}",
                RequestedAtUtc = now
            },
            "I want to run a command.",
            CancellationToken.None);

        var resolved = await toolApprovalService.ResolveApprovalAsync(created.Id, false, "Denied for test", CancellationToken.None);

        Assert.Equal("Failed", resolved.Approval.Status);
        Assert.Equal("Denied for test", resolved.Approval.DecisionComment);
        Assert.NotNull(resolved.AssistantMessage.Content);
        Assert.Null(resolved.PendingApproval);
    }
}
