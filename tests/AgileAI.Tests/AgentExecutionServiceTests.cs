using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using AgileAI.Studio.Api.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;

namespace AgileAI.Tests;

public class AgentExecutionServiceTests
{
    [Fact]
    public async Task SendMessageAsync_WithSkillRuntimeSuccess_ShouldPersistAssistantReply()
    {
        await using var harness = await AgentExecutionHarness.CreateAsync();
        harness.Agent.EnableSkills = true;
        harness.RuntimeMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult
            {
                IsSuccess = true,
                Output = "Skill reply"
            });

        var result = await harness.AgentExecutionService.SendMessageAsync(harness.Conversation.Id, "  hello from skill  ", CancellationToken.None);

        Assert.Equal("hello from skill", result.UserMessage.Content);
        Assert.Equal("Skill reply", result.AssistantMessage.Content);
        Assert.Equal(2, (await harness.DbContext.Messages.ToListAsync()).OrderBy(x => x.CreatedAtUtc).Count());
        harness.RuntimeMock.Verify(x => x.ExecuteAsync(
            It.Is<AgentRequest>(request =>
                request.SessionId == harness.Conversation.Id.ToString() &&
                request.ModelId == $"openai:{harness.Model.ModelKey}" &&
                request.Input == "hello from skill" &&
                request.EnableSkills),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithProviderLookupFailure_ShouldFallBackToStudioSession()
    {
        await using var harness = await AgentExecutionHarness.CreateAsync();
        harness.Agent.EnableSkills = true;
        harness.RuntimeMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult
            {
                IsSuccess = false,
                ErrorMessage = "Provider 'missing-provider' not found."
            });

        var result = await harness.AgentExecutionService.SendMessageAsync(harness.Conversation.Id, "Respond with OK", CancellationToken.None);

        Assert.Equal("OK", result.AssistantMessage.Content);
        Assert.NotEqual("Assistant session", result.Conversation.Title);
    }

    [Fact]
    public async Task SendMessageAsync_WithNonProviderRuntimeFailure_ShouldThrow()
    {
        await using var harness = await AgentExecutionHarness.CreateAsync();
        harness.Agent.EnableSkills = true;
        harness.RuntimeMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult
            {
                IsSuccess = false,
                ErrorMessage = "Planner blew up"
            });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.AgentExecutionService.SendMessageAsync(harness.Conversation.Id, "hello", CancellationToken.None));

        Assert.Equal("Planner blew up", error.Message);
    }

    [Fact]
    public async Task StreamMessageAsync_WithPlainChat_ShouldWriteSseAndPersistAssistantMessage()
    {
        await using var harness = await AgentExecutionHarness.CreateAsync();
        harness.Agent.EnableSkills = false;
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await harness.AgentExecutionService.StreamMessageAsync(harness.Conversation.Id, "tell me about the demo", httpContext.Response, CancellationToken.None);

        httpContext.Response.Body.Position = 0;
        var sse = Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
        Assert.Contains("event: message-created", sse);
        Assert.Contains("event: text-delta", sse);
        Assert.Contains("event: final-message", sse);
        Assert.Contains("event: completed", sse);

        var assistant = (await harness.DbContext.Messages
            .Where(x => x.Role == MessageRole.Assistant)
            .ToListAsync())
            .OrderByDescending(x => x.CreatedAtUtc)
            .First();
        Assert.False(assistant.IsStreaming);
        Assert.Contains("Mock response from AgileAI Studio", assistant.Content);
    }

    [Fact]
    public async Task SendMessageAsync_WithApprovalRequiredTool_ShouldCreatePendingApprovalAndPersistAssistantPlaceholder()
    {
        await using var harness = await AgentExecutionHarness.CreateAsync();
        harness.Agent.EnableSkills = false;

        var result = await harness.AgentExecutionService.SendMessageAsync(harness.Conversation.Id, "please run local command approval", CancellationToken.None);

        var approval = await harness.DbContext.ToolApprovalRequests.SingleAsync();
        Assert.Equal(ToolApprovalStatus.Pending, approval.Status);
        Assert.Equal("run_local_command", approval.ToolName);
        Assert.Contains("Command approval required for run_local_command", result.AssistantMessage.Content);

        var assistant = (await harness.DbContext.Messages
            .Where(x => x.Role == MessageRole.Assistant)
            .ToListAsync())
            .OrderByDescending(x => x.CreatedAtUtc)
            .First();
        Assert.Contains("Command approval required for run_local_command", assistant.Content);
    }

    private sealed class AgentExecutionHarness : IAsyncDisposable
    {
        private AgentExecutionHarness(
            SqliteConnection connection,
            StudioDbContext dbContext,
            ProviderConnection provider,
            StudioModel model,
            AgentDefinition agent,
            Conversation conversation,
            Mock<IAgentRuntime> runtimeMock,
            AgentExecutionService agentExecutionService)
        {
            Connection = connection;
            DbContext = dbContext;
            Provider = provider;
            Model = model;
            Agent = agent;
            Conversation = conversation;
            RuntimeMock = runtimeMock;
            AgentExecutionService = agentExecutionService;
        }

        public SqliteConnection Connection { get; }
        public StudioDbContext DbContext { get; }
        public ProviderConnection Provider { get; }
        public StudioModel Model { get; }
        public AgentDefinition Agent { get; }
        public Conversation Conversation { get; }
        public Mock<IAgentRuntime> RuntimeMock { get; }
        public AgentExecutionService AgentExecutionService { get; }

        public static async Task<AgentExecutionHarness> CreateAsync()
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
                Name = "Assistant",
                Description = "demo agent",
                SystemPrompt = "You are helpful.",
                Temperature = 0.2,
                MaxTokens = 256,
                EnableSkills = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                AgentDefinition = agent,
                AgentDefinitionId = agent.Id,
                Title = "Assistant session",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Messages = []
            };

            dbContext.ProviderConnections.Add(provider);
            dbContext.Models.Add(model);
            dbContext.Agents.Add(agent);
            dbContext.Conversations.Add(conversation);
            await dbContext.SaveChangesAsync();

            var skillRegistry = new InMemorySkillRegistry();
            var sessionStore = new InMemorySessionStore();
            var skillService = new SkillService(skillRegistry, sessionStore);
            var conversationService = new ConversationService(dbContext, skillService);
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
                new RunLocalCommandTool(new ProcessExecutionService()),
                new WebFetchTool(webFetchHttpClient));
            var agentService = new AgentService(dbContext, modelCatalogService, studioRegistryFactory, skillRegistry);
            var providerClientFactory = new ProviderClientFactory(NullLoggerFactory.Instance);
            var runtimeMock = new Mock<IAgentRuntime>(MockBehavior.Strict);
            var plannerMock = new Mock<ISkillPlanner>();
            plannerMock
                .Setup(x => x.PlanAsync(It.IsAny<AgentRequest>(), It.IsAny<IReadOnlyList<ISkill>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SkillPlan { ShouldUseSkill = false });
            var continuationMock = new Mock<ISkillContinuationPolicy>();
            continuationMock
                .Setup(x => x.DecideAsync(It.IsAny<AgentRequest>(), It.IsAny<ConversationState?>(), It.IsAny<IReadOnlyList<ISkill>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SkillContinuationDecision.NoContinuation());
            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            var toolApprovalService = new ToolApprovalService(
                dbContext,
                conversationService,
                agentService,
                modelCatalogService,
                providerClientFactory,
                serviceProvider,
                studioRegistryFactory);
            var agentExecutionService = new AgentExecutionService(
                conversationService,
                agentService,
                modelCatalogService,
                providerClientFactory,
                serviceProvider,
                runtimeMock.Object,
                skillRegistry,
                sessionStore,
                plannerMock.Object,
                continuationMock.Object,
                studioRegistryFactory,
                new StudioToolExecutionGate(),
                toolApprovalService);

            return new AgentExecutionHarness(connection, dbContext, provider, model, agent, conversation, runtimeMock, agentExecutionService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
