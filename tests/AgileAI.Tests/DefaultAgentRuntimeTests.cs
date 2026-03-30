using Moq;
using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.Tests;

public class DefaultAgentRuntimeTests
{
    [Fact]
    public void DefaultAgentRuntime_Constructor_ShouldInitializeCorrectly()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object);

        Assert.NotNull(runtime);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidInput_ShouldReturnSuccess()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var expectedResponse = new ChatResponse
        {
            IsSuccess = true,
            Message = new ChatMessage { Role = ChatRole.Assistant, TextContent = "Hello!" }
        };

        mockChatClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object);
        var request = new AgentRequest { Input = "Hi", ModelId = "test-model" };

        var result = await runtime.ExecuteAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello!", result.Output);
        mockChatClient.Verify(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithChatClientFailure_ShouldReturnError()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var expectedResponse = new ChatResponse { IsSuccess = false, ErrorMessage = "Test error" };

        mockChatClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object);
        var request = new AgentRequest { Input = "Hi", ModelId = "test-model" };

        var result = await runtime.ExecuteAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Test error", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithHistory_ShouldPreserveHistory()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var history = new List<ChatMessage>
        {
            ChatMessage.User("Previous message")
        };
        var expectedResponse = new ChatResponse
        {
            IsSuccess = true,
            Message = new ChatMessage { Role = ChatRole.Assistant, TextContent = "Updated response" }
        };

        mockChatClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object);
        var request = new AgentRequest { Input = "New input", ModelId = "test-model", History = history };

        var result = await runtime.ExecuteAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.UpdatedHistory);
        Assert.Equal(3, result.UpdatedHistory.Count);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutModelId_ShouldThrow()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object);
        var request = new AgentRequest { Input = "Hi" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ExecuteAsync(request));
    }

    [Fact]
    public async Task ExecuteAsync_WithSessionId_ShouldLoadAndPersistConversationState()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var sessionStore = new InMemorySessionStore();

        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = "session-1",
            History = [ChatMessage.User("Earlier")],
            UpdatedAt = DateTimeOffset.UtcNow
        });

        mockChatClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = ChatMessage.Assistant("Loaded from session")
            });

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object, sessionStore: sessionStore);
        var result = await runtime.ExecuteAsync(new AgentRequest
        {
            Input = "Now",
            ModelId = "test-model",
            SessionId = "session-1"
        });

        Assert.True(result.IsSuccess);
        var state = await sessionStore.GetAsync("session-1");
        Assert.NotNull(state);
        Assert.Equal(3, state!.History.Count);
        Assert.Null(state.ActiveSkill);
    }

    [Fact]
    public async Task ExecuteAsync_WithActiveSkillContinuation_ShouldReuseActiveSkillWithoutPlanner()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var mockSkill = new Mock<ISkill>();
        var mockPlanner = new Mock<ISkillPlanner>(MockBehavior.Strict);
        var sessionStore = new InMemorySessionStore();
        var continuationPolicy = new DefaultSkillContinuationPolicy();

        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = "session-2",
            ActiveSkill = "weather",
            History = [ChatMessage.User("Earlier")],
            UpdatedAt = DateTimeOffset.UtcNow
        });

        mockSkill.SetupGet(x => x.Name).Returns("weather");
        mockSkill.SetupGet(x => x.Description).Returns("Weather helper");
        mockSkill.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "weather" });
        mockSkill
            .Setup(x => x.ExecuteAsync(It.IsAny<SkillExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult
            {
                IsSuccess = true,
                Output = "continued",
                UpdatedHistory = [ChatMessage.User("Earlier"), ChatMessage.Assistant("continued")]
            });

        mockSkillRegistry.Setup(x => x.GetAllSkills()).Returns([mockSkill.Object]);
        var weatherSkill = mockSkill.Object;
        mockSkillRegistry.Setup(x => x.TryGetSkill("weather", out weatherSkill)).Returns(true);

        var runtime = new DefaultAgentRuntime(
            mockChatClient.Object,
            mockSkillRegistry.Object,
            skillPlanner: mockPlanner.Object,
            sessionStore: sessionStore,
            skillContinuationPolicy: continuationPolicy);

        var result = await runtime.ExecuteAsync(new AgentRequest
        {
            Input = "continue",
            ModelId = "test-model",
            SessionId = "session-2",
            EnableSkills = true
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("continued", result.Output);
        mockSkill.Verify(x => x.ExecuteAsync(It.IsAny<SkillExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        var state = await sessionStore.GetAsync("session-2");
        Assert.Equal("weather", state!.ActiveSkill);
    }

    [Fact]
    public async Task ExecuteAsync_PlainChatAfterSkill_ShouldClearActiveSkill()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var sessionStore = new InMemorySessionStore();

        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = "session-3",
            ActiveSkill = "weather",
            History = [ChatMessage.User("Earlier")],
            UpdatedAt = DateTimeOffset.UtcNow
        });

        mockChatClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = ChatMessage.Assistant("plain response")
            });

        var runtime = new DefaultAgentRuntime(mockChatClient.Object, mockSkillRegistry.Object, sessionStore: sessionStore);
        var result = await runtime.ExecuteAsync(new AgentRequest
        {
            Input = "plain chat",
            ModelId = "test-model",
            SessionId = "session-3",
            EnableSkills = false
        });

        Assert.True(result.IsSuccess);
        var state = await sessionStore.GetAsync("session-3");
        Assert.NotNull(state);
        Assert.Null(state!.ActiveSkill);
    }

    [Fact]
    public async Task ExecuteAsync_PreferredSkillSwitch_ShouldPersistNewActiveSkill()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var oldSkill = new Mock<ISkill>();
        var newSkill = new Mock<ISkill>();
        var mockPlanner = new Mock<ISkillPlanner>(MockBehavior.Strict);
        var sessionStore = new InMemorySessionStore();
        var continuationPolicy = new DefaultSkillContinuationPolicy();

        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = "session-4",
            ActiveSkill = "weather",
            History = [ChatMessage.User("Earlier")],
            UpdatedAt = DateTimeOffset.UtcNow
        });

        oldSkill.SetupGet(x => x.Name).Returns("weather");
        newSkill.SetupGet(x => x.Name).Returns("calendar");
        newSkill.SetupGet(x => x.Description).Returns("Calendar helper");
        newSkill.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "calendar" });
        newSkill
            .Setup(x => x.ExecuteAsync(It.IsAny<SkillExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult
            {
                IsSuccess = true,
                Output = "switched",
                UpdatedHistory = [ChatMessage.User("Earlier"), ChatMessage.Assistant("switched")]
            });

        mockSkillRegistry.Setup(x => x.GetAllSkills()).Returns([oldSkill.Object, newSkill.Object]);
        var calendarSkill = newSkill.Object;
        mockSkillRegistry.Setup(x => x.TryGetSkill("calendar", out calendarSkill)).Returns(true);

        var runtime = new DefaultAgentRuntime(
            mockChatClient.Object,
            mockSkillRegistry.Object,
            skillPlanner: mockPlanner.Object,
            sessionStore: sessionStore,
            skillContinuationPolicy: continuationPolicy);

        var result = await runtime.ExecuteAsync(new AgentRequest
        {
            Input = "switch",
            ModelId = "test-model",
            SessionId = "session-4",
            EnableSkills = true,
            PreferredSkill = "calendar"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("switched", result.Output);
        var state = await sessionStore.GetAsync("session-4");
        Assert.NotNull(state);
        Assert.Equal("calendar", state!.ActiveSkill);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotContinueDisallowedActiveSkill()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockSkillRegistry = new Mock<ISkillRegistry>();
        var mockSkill = new Mock<ISkill>();
        var sessionStore = new InMemorySessionStore();
        var continuationPolicy = new DefaultSkillContinuationPolicy();

        await sessionStore.SaveAsync(new ConversationState
        {
            SessionId = "session-allowlist-1",
            ActiveSkill = "weather",
            History = [ChatMessage.User("Earlier")],
            UpdatedAt = DateTimeOffset.UtcNow
        });

        mockSkill.SetupGet(x => x.Name).Returns("weather");
        mockSkill.SetupGet(x => x.Description).Returns("Weather helper");
        mockSkill.SetupGet(x => x.Manifest).Returns(new SkillManifest { Name = "weather" });

        mockSkillRegistry.Setup(x => x.GetAllSkills()).Returns([mockSkill.Object]);
        var weatherSkill = mockSkill.Object;
        mockSkillRegistry.Setup(x => x.TryGetSkill("weather", out weatherSkill)).Returns(true);

        mockChatClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = ChatMessage.Assistant("plain response")
            });

        var runtime = new DefaultAgentRuntime(
            mockChatClient.Object,
            mockSkillRegistry.Object,
            sessionStore: sessionStore,
            skillContinuationPolicy: continuationPolicy);

        var result = await runtime.ExecuteAsync(new AgentRequest
        {
            Input = "continue",
            ModelId = "test-model",
            SessionId = "session-allowlist-1",
            EnableSkills = true,
            AllowedSkills = ["repo-guide"]
        });

        Assert.True(result.IsSuccess);
        mockSkill.Verify(x => x.ExecuteAsync(It.IsAny<SkillExecutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        mockChatClient.Verify(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var state = await sessionStore.GetAsync("session-allowlist-1");
        Assert.NotNull(state);
        Assert.Null(state!.ActiveSkill);
    }
}
