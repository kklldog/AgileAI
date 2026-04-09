using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace AgileAI.Tests;

public class ToolApprovalServiceStreamingTests
{
    [Fact]
    public async Task StreamApprovalResolutionAsync_WithDeniedApproval_ShouldWriteFinalMessageAndComplete()
    {
        await using var harness = await StreamingApprovalHarness.CreateAsync();
        var approval = await harness.ToolApprovalService.CreatePendingApprovalAsync(
            harness.Conversation,
            harness.AssistantMessage.Id,
            new AgileAI.Abstractions.ToolApprovalRequest
            {
                ToolCallId = "tool-call-1",
                ToolName = "run_local_command",
                Arguments = "{\"command\":\"echo hi\"}",
                RequestedAtUtc = DateTimeOffset.UtcNow
            },
            "I want to run a command.",
            CancellationToken.None);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await harness.ToolApprovalService.StreamApprovalResolutionAsync(approval.Id, false, "Denied in stream", httpContext.Response, CancellationToken.None);

        httpContext.Response.Body.Position = 0;
        var sse = Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
        Assert.Contains("event: text-delta", sse);
        Assert.Contains("event: final-message", sse);
        Assert.Contains("event: completed", sse);

        var updatedApproval = await harness.DbContext.ToolApprovalRequests.SingleAsync();
        Assert.Equal(ToolApprovalStatus.Failed, updatedApproval.Status);
        Assert.Equal("Denied in stream", updatedApproval.DecisionComment);
        Assert.Contains("Denied in stream", updatedApproval.ResultContent);
    }

    private sealed class StreamingApprovalHarness : IAsyncDisposable
    {
        private StreamingApprovalHarness(
            SqliteConnection connection,
            StudioDbContext dbContext,
            Conversation conversation,
            ConversationMessage assistantMessage,
            ToolApprovalService toolApprovalService)
        {
            Connection = connection;
            DbContext = dbContext;
            Conversation = conversation;
            AssistantMessage = assistantMessage;
            ToolApprovalService = toolApprovalService;
        }

        public SqliteConnection Connection { get; }
        public StudioDbContext DbContext { get; }
        public Conversation Conversation { get; }
        public ConversationMessage AssistantMessage { get; }
        public ToolApprovalService ToolApprovalService { get; }

        public static async Task<StreamingApprovalHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StudioDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new StudioDbContext(options);
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

            var skillRegistry = new InMemorySkillRegistry();
            var sessionStore = new InMemorySessionStore();
            var skillService = new SkillService(skillRegistry, sessionStore);
            var conversationService = new ConversationService(dbContext, skillService);
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
            var processExecutionService = new AgileAI.Core.ProcessExecutionService();
            var studioRegistryFactory = new StudioToolRegistryFactory(
                fileSystemFactory,
                new RunLocalCommandTool(processExecutionService),
                new WebFetchTool(webFetchHttpClient));
            var modelCatalogService = new ModelCatalogService(dbContext, new ProviderClientFactory(NullLoggerFactory.Instance));
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

            return new StreamingApprovalHarness(connection, dbContext, conversation, assistantMessage, toolApprovalService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
