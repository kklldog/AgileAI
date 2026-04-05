using System.Runtime.CompilerServices;
using AgileAI.Abstractions;

namespace AgileAI.Studio.Api.Services;

public class MockChatModelProvider(string providerName) : IChatModelProvider
{
    public string ProviderName { get; } = providerName;

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var toolResponse = BuildToolResponse(request);
        if (toolResponse != null)
        {
            return Task.FromResult(toolResponse);
        }

        var output = BuildReply(request);
        return Task.FromResult(new ChatResponse
        {
            IsSuccess = true,
            FinishReason = "stop",
            Usage = new UsageInfo
            {
                PromptTokens = Math.Max(8, output.Length / 4),
                CompletionTokens = Math.Max(10, output.Length / 5),
                TotalTokens = Math.Max(18, output.Length / 2)
            },
            Message = ChatMessage.Assistant(output)
        });
    }

    public async IAsyncEnumerable<StreamingChatUpdate> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var toolResponse = BuildToolResponse(request);
        if (toolResponse?.Message?.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in toolResponse.Message.ToolCalls)
            {
                yield return new ToolCallDeltaUpdate(
                    toolCall.Id,
                    toolCall.Name,
                    toolCall.Arguments);
            }
            yield return new CompletedUpdate("tool_calls");
            yield break;
        }

        var output = BuildReply(request);
        foreach (var chunk in Chunk(output, 18))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(18, cancellationToken);
            yield return new TextDeltaUpdate(chunk);
        }

        yield return new UsageUpdate(new UsageInfo
        {
            PromptTokens = Math.Max(8, output.Length / 4),
            CompletionTokens = Math.Max(10, output.Length / 5),
            TotalTokens = Math.Max(18, output.Length / 2)
        });
        yield return new CompletedUpdate("stop");
    }

    private static string BuildReply(ChatRequest request)
    {
        var lastTool = request.Messages.LastOrDefault(x => x.Role == ChatRole.Tool)?.TextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(lastTool))
        {
            return $"Workspace tool completed successfully. Here is the result:\n\n{lastTool}";
        }

        var lastUser = request.Messages.LastOrDefault(x => x.Role == ChatRole.User)?.TextContent?.Trim() ?? "";
        if (lastUser.Contains("Respond with OK", StringComparison.OrdinalIgnoreCase))
        {
            return "OK";
        }

        return $"Mock response from AgileAI Studio for: {lastUser}\n\n- This is a local demo reply\n- Streaming and persistence are active\n- Replace the seeded API key to use a real provider";
    }

    private static ChatResponse? BuildToolResponse(ChatRequest request)
    {
        var lastTool = request.Messages.LastOrDefault(x => x.Role == ChatRole.Tool);
        if (lastTool != null)
        {
            return null;
        }

        var lastUser = request.Messages.LastOrDefault(x => x.Role == ChatRole.User)?.TextContent?.Trim() ?? string.Empty;
        if (request.Options?.Tools == null || request.Options.Tools.Count == 0 || string.IsNullOrWhiteSpace(lastUser))
        {
            return null;
        }

        if (lastUser.Contains("readme", StringComparison.OrdinalIgnoreCase))
        {
            return ToolCallResponse("read_file", "{\"path\":\"README.md\"}");
        }

        if (lastUser.Contains("batch", StringComparison.OrdinalIgnoreCase) || lastUser.Contains("multiple files", StringComparison.OrdinalIgnoreCase))
        {
            return ToolCallResponse("read_files_batch", "{\"paths\":[\"README.md\",\"samples/FileSystemToolsSample/Program.cs\"]}");
        }

        if (lastUser.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return ToolCallResponse("search_files", "{\"path\":\".\",\"query\":\"AgileAI.Studio\",\"limit\":5}");
        }

        if (lastUser.Contains("list", StringComparison.OrdinalIgnoreCase) && lastUser.Contains("director", StringComparison.OrdinalIgnoreCase))
        {
            return ToolCallResponse("list_directory", "{\"path\":\".\"}");
        }

        if (lastUser.Contains("write file", StringComparison.OrdinalIgnoreCase) || lastUser.Contains("write a file", StringComparison.OrdinalIgnoreCase))
        {
            return ToolCallResponse("write_file", "{\"path\":\"tmp/studio-tool-smoke.txt\",\"content\":\"Studio workspace tool smoke test.\"}");
        }

        if (lastUser.Contains("run local command", StringComparison.OrdinalIgnoreCase) ||
            lastUser.Contains("command approval", StringComparison.OrdinalIgnoreCase))
        {
            return ToolCallResponse("run_local_command", "{\"command\":\"echo Playwright command ok\",\"shell\":\"auto\",\"timeoutMs\":120000}");
        }

        return null;
    }

    private static ChatResponse ToolCallResponse(string toolName, string arguments)
        => new()
        {
            IsSuccess = true,
            FinishReason = "tool_calls",
            Message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                ToolCalls =
                [
                    new ToolCall
                    {
                        Id = "tool-call-1",
                        Name = toolName,
                        Arguments = arguments
                    }
                ]
            }
        };

    private static IEnumerable<string> Chunk(string input, int size)
    {
        for (var index = 0; index < input.Length; index += size)
        {
            yield return input.Substring(index, Math.Min(size, input.Length - index));
        }
    }
}
