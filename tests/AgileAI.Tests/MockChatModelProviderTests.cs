using AgileAI.Abstractions;
using AgileAI.Studio.Api.Services;

namespace AgileAI.Tests;

public class MockChatModelProviderTests
{
    [Fact]
    public async Task CompleteAsync_WithConnectivityPrompt_ShouldReplyOk()
    {
        var provider = new MockChatModelProvider("mock");

        var response = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "mock:model",
            Messages = [ChatMessage.User("Respond with OK")]
        });

        Assert.True(response.IsSuccess);
        Assert.Equal("OK", response.Message?.TextContent);
        Assert.Equal("stop", response.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_WithReadmeRequestAndTools_ShouldReturnToolCall()
    {
        var provider = new MockChatModelProvider("mock");

        var response = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "mock:model",
            Messages = [ChatMessage.User("Please open the readme")],
            Options = new ChatOptions
            {
                Tools = [new ToolDefinition { Name = "read_file", Description = "Read a file" }]
            }
        });

        Assert.True(response.IsSuccess);
        Assert.Equal("tool_calls", response.FinishReason);
        Assert.Single(response.Message!.ToolCalls!);
        Assert.Equal("read_file", response.Message.ToolCalls[0].Name);
        Assert.Contains("README.md", response.Message.ToolCalls[0].Arguments);
    }

    [Fact]
    public async Task CompleteAsync_WithPriorToolResult_ShouldSummarizeToolOutput()
    {
        var provider = new MockChatModelProvider("mock");

        var response = await provider.CompleteAsync(new ChatRequest
        {
            ModelId = "mock:model",
            Messages =
            [
                ChatMessage.User("search for readme"),
                new ChatMessage { Role = ChatRole.Tool, ToolCallId = "tool-call-1", TextContent = "Tool returned lines" }
            ]
        });

        Assert.True(response.IsSuccess);
        Assert.Contains("Workspace tool completed successfully", response.Message?.TextContent);
        Assert.Contains("Tool returned lines", response.Message?.TextContent);
    }

    [Fact]
    public async Task StreamAsync_WithToolRequest_ShouldEmitCompletedToolCallOnly()
    {
        var provider = new MockChatModelProvider("mock");
        var updates = new List<StreamingChatUpdate>();

        await foreach (var update in provider.StreamAsync(new ChatRequest
        {
            ModelId = "mock:model",
            Messages = [ChatMessage.User("please run local command approval")],
            Options = new ChatOptions
            {
                Tools = [new ToolDefinition { Name = "run_local_command", Description = "Run command" }]
            }
        }))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, update => update is ToolCallDeltaUpdate toolCall && toolCall.ToolCallId == "tool-call-1" && toolCall.NameDelta == "run_local_command");
        var completed = Assert.IsType<CompletedUpdate>(updates[^1]);
        Assert.IsType<CompletedUpdate>(completed);
        Assert.Equal("tool_calls", completed.FinishReason);
    }

    [Fact]
    public async Task StreamAsync_WithStandardReply_ShouldEmitTextUsageAndCompletion()
    {
        var provider = new MockChatModelProvider("mock");
        var updates = new List<StreamingChatUpdate>();

        await foreach (var update in provider.StreamAsync(new ChatRequest
        {
            ModelId = "mock:model",
            Messages = [ChatMessage.User("tell me about the demo")]
        }))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, update => update is TextDeltaUpdate);
        Assert.Contains(updates, update => update is UsageUpdate);
        Assert.IsType<CompletedUpdate>(updates[^1]);
        Assert.Equal("stop", ((CompletedUpdate)updates[^1]).FinishReason);
    }
}
