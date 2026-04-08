using AgileAI.Abstractions;
using AgileAI.Core;
using Moq;

namespace AgileAI.Tests;

public class ChatSessionTests
{
    [Fact]
    public async Task SendAsync_ShouldAddUserMessageToHistory()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = ChatMessage.Assistant("response") });

        var session = new ChatSession(mockChatClient.Object, "test-model");

        await session.SendAsync("hello");

        Assert.Equal(2, session.History.Count);
        Assert.Equal(ChatRole.User, session.History[0].Role);
        Assert.Equal("hello", session.History[0].TextContent);
    }

    [Fact]
    public async Task SendAsync_ShouldAddAssistantMessageToHistory()
    {
        var mockChatClient = new Mock<IChatClient>();
        var assistantMessage = ChatMessage.Assistant("response");
        mockChatClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = assistantMessage });

        var session = new ChatSession(mockChatClient.Object, "test-model");

        await session.SendAsync("hello");

        Assert.Equal(2, session.History.Count);
        Assert.Equal(ChatRole.Assistant, session.History[1].Role);
        Assert.Equal("response", session.History[1].TextContent);
    }

    [Fact]
    public async Task SendAsync_WithToolCalls_ShouldExecuteTools()
    {
        var toolCallId = "call_123";
        var toolName = "test-tool";
        var mockChatClient = new Mock<IChatClient>();
        var toolCall = new ToolCall { Id = toolCallId, Name = toolName, Arguments = "{}" };
        var assistantMessage = new ChatMessage 
        { 
            Role = ChatRole.Assistant, 
            ToolCalls = new List<ToolCall> { toolCall } 
        };

        mockChatClient.SetupSequence(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = assistantMessage })
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = ChatMessage.Assistant("final response") });

        var mockTool = new Mock<ITool>();
        mockTool.Setup(t => t.Name).Returns(toolName);
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { ToolCallId = toolCallId, Content = "tool result", IsSuccess = true });

        var toolRegistry = new InMemoryToolRegistry();
        toolRegistry.Register(mockTool.Object);

        var session = new ChatSession(mockChatClient.Object, "test-model", toolRegistry);
        var response = await session.SendAsync("hello");

        Assert.True(response.IsSuccess);
        mockTool.Verify(t => t.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(4, session.History.Count);
        Assert.Equal(ChatRole.Tool, session.History[2].Role);
        Assert.Equal("tool result", session.History[2].TextContent);
    }

    [Fact]
    public async Task SendAsync_WithUnknownTool_ShouldReturnErrorToolResult()
    {
        var toolCallId = "call_123";
        var toolName = "unknown-tool";
        var mockChatClient = new Mock<IChatClient>();
        var toolCall = new ToolCall { Id = toolCallId, Name = toolName, Arguments = "{}" };
        var assistantMessage = new ChatMessage 
        { 
            Role = ChatRole.Assistant, 
            ToolCalls = new List<ToolCall> { toolCall } 
        };

        mockChatClient.SetupSequence(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = assistantMessage })
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = ChatMessage.Assistant("final response") });

        var toolRegistry = new InMemoryToolRegistry();

        var session = new ChatSession(mockChatClient.Object, "test-model", toolRegistry);
        await session.SendAsync("hello");

        Assert.Equal(4, session.History.Count);
        Assert.Equal(ChatRole.Tool, session.History[2].Role);
        Assert.Contains("not found", session.History[2].TextContent);
    }

    [Fact]
    public void AddMessage_ShouldAddToHistory()
    {
        var mockChatClient = new Mock<IChatClient>();
        var session = new ChatSession(mockChatClient.Object, "test-model");
        var message = ChatMessage.User("test message");

        session.AddMessage(message);

        Assert.Single(session.History);
        Assert.Equal(message, session.History[0]);
    }

    [Fact]
    public void ClearHistory_ShouldEmptyHistory()
    {
        var mockChatClient = new Mock<IChatClient>();
        var session = new ChatSession(mockChatClient.Object, "test-model");
        session.AddMessage(ChatMessage.User("test"));

        session.ClearHistory();

        Assert.Empty(session.History);
    }

    [Fact]
    public async Task SendAsync_WithToolThrowingException_ShouldReturnErrorToolResult()
    {
        var toolCallId = "call_123";
        var toolName = "error-tool";
        var mockChatClient = new Mock<IChatClient>();
        var toolCall = new ToolCall { Id = toolCallId, Name = toolName, Arguments = "{}" };
        var assistantMessage = new ChatMessage 
        { 
            Role = ChatRole.Assistant, 
            ToolCalls = new List<ToolCall> { toolCall } 
        };

        mockChatClient.SetupSequence(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = assistantMessage })
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = ChatMessage.Assistant("final response") });

        var mockTool = new Mock<ITool>();
        mockTool.Setup(t => t.Name).Returns(toolName);
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        var toolRegistry = new InMemoryToolRegistry();
        toolRegistry.Register(mockTool.Object);

        var session = new ChatSession(mockChatClient.Object, "test-model", toolRegistry);
        await session.SendAsync("hello");

        Assert.Equal(4, session.History.Count);
        Assert.Equal(ChatRole.Tool, session.History[2].Role);
        Assert.Contains("Something went wrong", session.History[2].TextContent);
    }

    [Fact]
    public async Task SendAsync_WithMaxToolLoopIterationsReached_ShouldStop()
    {
        var toolCallId = "call_123";
        var toolName = "loop-tool";
        var mockChatClient = new Mock<IChatClient>();
        var toolCall = new ToolCall { Id = toolCallId, Name = toolName, Arguments = "{}" };
        var assistantMessageWithTool = new ChatMessage 
        { 
            Role = ChatRole.Assistant, 
            ToolCalls = new List<ToolCall> { toolCall } 
        };

        mockChatClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse { IsSuccess = true, Message = assistantMessageWithTool });

        var mockTool = new Mock<ITool>();
        mockTool.Setup(t => t.Name).Returns(toolName);
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { ToolCallId = toolCallId, Content = "loop result", IsSuccess = true });

        var toolRegistry = new InMemoryToolRegistry();
        toolRegistry.Register(mockTool.Object);

        var maxIterations = 2;
        var session = new ChatSession(mockChatClient.Object, "test-model", toolRegistry, maxIterations);
        var response = await session.SendAsync("hello");

        Assert.True(response.IsSuccess);
        mockChatClient.Verify(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(maxIterations));
    }

    [Fact]
    public async Task SendTurnAsync_WithApprovalRequired_ShouldReturnPendingApprovalWithoutExecutingTool()
    {
        var toolCall = new ToolCall { Id = "call-approval", Name = "approval-tool", Arguments = "{}" };
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    TextContent = "Need to run a gated tool.",
                    ToolCalls = [toolCall]
                }
            });

        var tool = new ApprovalAwareTestTool("approval-tool", "approved-result");
        var registry = new InMemoryToolRegistry();
        registry.Register(tool);

        var session = new ChatSession(
            mockChatClient.Object,
            "test-model",
            registry,
            toolExecutionGate: new PendingExecutionGate());

        var result = await session.SendTurnAsync("hello");

        Assert.NotNull(result.PendingApprovalRequest);
        Assert.Equal("approval-tool", result.PendingApprovalRequest!.ToolName);
        Assert.Equal(0, tool.ExecutionCount);
        Assert.Equal(2, session.History.Count);
        Assert.Equal(ChatRole.Assistant, session.History[1].Role);
    }

    [Fact]
    public async Task SendTurnAsync_WithAttributeMarkedTool_ShouldReturnPendingApprovalWithoutExecutingTool()
    {
        var toolCall = new ToolCall { Id = "call-attribute-approval", Name = "attribute-approval-tool", Arguments = "{}" };
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    TextContent = "Need to run an attribute-gated tool.",
                    ToolCalls = [toolCall]
                }
            });

        var tool = new AttributeApprovalTestTool("attribute-approval-tool", "approved-result");
        var registry = new InMemoryToolRegistry();
        registry.Register(tool);

        var session = new ChatSession(
            mockChatClient.Object,
            "test-model",
            registry,
            toolExecutionGate: new PendingExecutionGate());

        var result = await session.SendTurnAsync("hello");

        Assert.NotNull(result.PendingApprovalRequest);
        Assert.Equal("attribute-approval-tool", result.PendingApprovalRequest!.ToolName);
        Assert.Equal(0, tool.ExecutionCount);
        Assert.Equal(2, session.History.Count);
        Assert.Equal(ChatRole.Assistant, session.History[1].Role);
    }

    [Fact]
    public async Task ContinueAsync_AfterApprovedToolResult_ShouldResumeLoopAndSupportAnotherPendingApproval()
    {
        var firstToolCall = new ToolCall { Id = "call-1", Name = "approval-tool", Arguments = "{}" };
        var secondToolCall = new ToolCall { Id = "call-2", Name = "approval-tool", Arguments = "{}" };
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.SetupSequence(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    TextContent = "Need first approval.",
                    ToolCalls = [firstToolCall]
                }
            })
            .ReturnsAsync(new ChatResponse
            {
                IsSuccess = true,
                Message = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    TextContent = "Need second approval.",
                    ToolCalls = [secondToolCall]
                }
            });

        var tool = new ApprovalAwareTestTool("approval-tool", "approved-result");
        var registry = new InMemoryToolRegistry();
        registry.Register(tool);
        var gate = new SequenceExecutionGate(
            ToolApprovalDecision.PendingDecision("approval-1"),
            ToolApprovalDecision.PendingDecision("approval-2"));

        var session = new ChatSession(mockChatClient.Object, "test-model", registry, toolExecutionGate: gate);

        var initialTurn = await session.SendTurnAsync("start");
        Assert.NotNull(initialTurn.PendingApprovalRequest);

        session.AddMessage(new ChatMessage
        {
            Role = ChatRole.Tool,
            ToolCallId = firstToolCall.Id,
            TextContent = "approved-result"
        });

        var continuedTurn = await session.ContinueAsync();

        Assert.NotNull(continuedTurn.PendingApprovalRequest);
        Assert.Equal(secondToolCall.Id, continuedTurn.PendingApprovalRequest!.ToolCallId);
        Assert.Equal(0, tool.ExecutionCount);
        mockChatClient.Verify(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task StreamTurnAsync_WithPlainText_ShouldEmitDeltasAndCompleted()
    {
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.StreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Stream(
                new TextDeltaUpdate("Hel"),
                new TextDeltaUpdate("lo"),
                new UsageUpdate(new UsageInfo { PromptTokens = 5, CompletionTokens = 2 }),
                new CompletedUpdate("stop")));

        var session = new ChatSession(mockChatClient.Object, "test-model");

        var updates = new List<ChatTurnStreamUpdate>();
        await foreach (var update in session.StreamTurnAsync("hi"))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, x => x is ChatTurnTextDelta td && td.Delta == "Hel");
        Assert.Contains(updates, x => x is ChatTurnTextDelta td && td.Delta == "lo");
        Assert.Contains(updates, x => x is ChatTurnUsage usage && usage.Usage.PromptTokens == 5 && usage.Usage.CompletionTokens == 2);
        Assert.Contains(updates, x => x is ChatTurnCompleted completed && completed.Response.Message?.TextContent == "Hello" && completed.Response.FinishReason == "stop");
        Assert.Equal("Hello", session.History.Last().TextContent);
    }

    [Fact]
    public async Task StreamTurnAsync_WithToolCallAndPendingApproval_ShouldEmitTextAndPendingApproval()
    {
        var toolCall = new ToolCall { Id = "call-approval", Name = "approval-tool", Arguments = "{}" };
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.StreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Stream(
                new TextDeltaUpdate("Need approval."),
                new ToolCallDeltaUpdate(toolCall.Id, toolCall.Name, toolCall.Arguments),
                new CompletedUpdate("tool_calls")));

        var tool = new ApprovalAwareTestTool("approval-tool", "approved-result");
        var registry = new InMemoryToolRegistry();
        registry.Register(tool);

        var session = new ChatSession(
            mockChatClient.Object,
            "test-model",
            registry,
            toolExecutionGate: new PendingExecutionGate());

        var updates = new List<ChatTurnStreamUpdate>();
        await foreach (var update in session.StreamTurnAsync("hello"))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, x => x is ChatTurnTextDelta td && td.Delta == "Need approval.");
        var pending = Assert.Single(updates.OfType<ChatTurnPendingApproval>());
        Assert.Equal("approval-tool", pending.PendingApprovalRequest.ToolName);
        Assert.Equal("Need approval.", pending.Response.Message?.TextContent);
        Assert.Equal(["approval-tool"], pending.ToolNames);
        Assert.Equal(0, tool.ExecutionCount);
        Assert.Equal(ChatRole.Assistant, session.History.Last().Role);
    }

    [Fact]
    public async Task ContinueStreamAsync_AfterApprovedToolResult_ShouldResumeWithDeltas()
    {
        var firstToolCall = new ToolCall { Id = "call-1", Name = "approval-tool", Arguments = "{}" };
        var secondResponseText = "All done";
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.SetupSequence(c => c.StreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Stream(
                new TextDeltaUpdate("Need first approval."),
                new ToolCallDeltaUpdate(firstToolCall.Id, firstToolCall.Name, firstToolCall.Arguments),
                new CompletedUpdate("tool_calls")))
            .Returns(Stream(
                new TextDeltaUpdate("All "),
                new TextDeltaUpdate("done"),
                new CompletedUpdate("stop")));

        var tool = new ApprovalAwareTestTool("approval-tool", "approved-result");
        var registry = new InMemoryToolRegistry();
        registry.Register(tool);
        var gate = new SequenceExecutionGate(
            ToolApprovalDecision.PendingDecision("approval-1"),
            ToolApprovalDecision.ApprovedDecision("approval-1"));

        var session = new ChatSession(mockChatClient.Object, "test-model", registry, toolExecutionGate: gate);

        await foreach (var _ in session.StreamTurnAsync("start"))
        {
        }

        session.AddMessage(new ChatMessage
        {
            Role = ChatRole.Tool,
            ToolCallId = firstToolCall.Id,
            TextContent = "approved-result"
        });

        var updates = new List<ChatTurnStreamUpdate>();
        await foreach (var update in session.ContinueStreamAsync())
        {
            updates.Add(update);
        }

        Assert.Contains(updates, x => x is ChatTurnTextDelta td && td.Delta == "All ");
        Assert.Contains(updates, x => x is ChatTurnTextDelta td && td.Delta == "done");
        Assert.Contains(updates, x => x is ChatTurnCompleted completed && completed.Response.Message?.TextContent == secondResponseText);
        Assert.Equal(secondResponseText, session.History.Last().TextContent);
    }

    private static async IAsyncEnumerable<StreamingChatUpdate> Stream(params StreamingChatUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }

    private sealed class ApprovalAwareTestTool(string name, string resultContent) : ITool, IApprovalAwareTool
    {
        public string Name => name;
        public string? Description => "approval-aware test tool";
        public object? ParametersSchema => new { };
        public ToolApprovalMode ApprovalMode => ToolApprovalMode.PerExecution;
        public int ExecutionCount { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolResult
            {
                ToolCallId = context.ToolCall.Id,
                Content = resultContent,
                IsSuccess = true
            });
        }
    }

    [NeedApproval]
    private sealed class AttributeApprovalTestTool(string name, string resultContent) : ITool
    {
        public string Name => name;
        public string? Description => "attribute approval test tool";
        public object? ParametersSchema => new { };
        public int ExecutionCount { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolResult
            {
                ToolCallId = context.ToolCall.Id,
                Content = resultContent,
                IsSuccess = true
            });
        }
    }

    private sealed class PendingExecutionGate : IToolExecutionGate
    {
        public Task<ToolApprovalDecision> EvaluateAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolApprovalDecision.PendingDecision(request.Id));
    }

    private sealed class SequenceExecutionGate(params ToolApprovalDecision[] decisions) : IToolExecutionGate
    {
        private readonly Queue<ToolApprovalDecision> _decisions = new(decisions);

        public Task<ToolApprovalDecision> EvaluateAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
        {
            var decision = _decisions.Count > 0
                ? _decisions.Dequeue()
                : ToolApprovalDecision.ApprovedDecision(request.Id);
            return Task.FromResult(decision with
            {
                ApprovalRequestId = string.IsNullOrWhiteSpace(decision.ApprovalRequestId) ? request.Id : decision.ApprovalRequestId
            });
        }
    }
}
