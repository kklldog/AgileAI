using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Domain;

namespace AgileAI.Studio.Api.Services;

public class AgentExecutionService(
    ConversationService conversationService,
    ModelCatalogService modelCatalogService,
    ProviderClientFactory providerClientFactory,
    FileSystemToolRegistryFactory toolRegistryFactory)
{
    public async Task<ChatResultDto> SendMessageAsync(Guid conversationId, string content, CancellationToken cancellationToken)
    {
        var conversation = await conversationService.GetConversationEntityAsync(conversationId, cancellationToken);
        var agent = conversation.AgentDefinition ?? throw new InvalidOperationException("Conversation agent is missing.");
        var runtime = await modelCatalogService.GetRuntimeOptionsAsync(agent.StudioModelId, cancellationToken);
        var chatClient = providerClientFactory.CreateClient(runtime);
        var trimmedContent = content.Trim();

        var userMessage = await conversationService.AddMessageAsync(conversation.Id, MessageRole.User, trimmedContent, false, null, null, null, cancellationToken);

        var session = CreateSession(conversation, agent, runtime.RuntimeModelId, chatClient);
        var response = await session.SendAsync(trimmedContent, new ChatOptions
        {
            Temperature = agent.Temperature,
            MaxTokens = agent.MaxTokens
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.ErrorMessage ?? "Model request failed.");
        }

        var assistantText = response.Message?.TextContent ?? string.Empty;
        var assistant = await conversationService.AddMessageAsync(
            conversation.Id,
            MessageRole.Assistant,
            assistantText,
            false,
            response.FinishReason,
            response.Usage?.PromptTokens,
            response.Usage?.CompletionTokens,
            cancellationToken);

        await conversationService.TouchConversationAsync(conversation, cancellationToken);

        return new ChatResultDto(
            ConversationService.MapConversation(conversation),
            ConversationService.MapMessage(userMessage),
            ConversationService.MapMessage(assistant));
    }

    public async Task StreamMessageAsync(Guid conversationId, string content, HttpResponse response, CancellationToken cancellationToken)
    {
        var conversation = await conversationService.GetConversationEntityAsync(conversationId, cancellationToken);
        var trimmedContent = content.Trim();
        var userMessage = await conversationService.AddMessageAsync(conversation.Id, MessageRole.User, trimmedContent, false, null, null, null, cancellationToken);
        var assistant = await conversationService.AddMessageAsync(conversation.Id, MessageRole.Assistant, string.Empty, true, null, null, null, cancellationToken);

        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        await WriteSseAsync(response, "message-created", new
        {
            userMessage = ConversationService.MapMessage(userMessage),
            assistantMessage = ConversationService.MapMessage(assistant),
            conversation = ConversationService.MapConversation(conversation)
        }, cancellationToken);

        try
        {
            var result = await SendMessageWithSessionAsync(conversation, trimmedContent, cancellationToken);
            await conversationService.UpdateMessageAsync(
                assistant,
                result.AssistantContent,
                false,
                result.FinishReason,
                result.InputTokens,
                result.OutputTokens,
                cancellationToken);
            await WriteSseAsync(response, "final-message", new
            {
                content = result.AssistantContent,
                finishReason = result.FinishReason,
                inputTokens = result.InputTokens,
                outputTokens = result.OutputTokens
            }, cancellationToken);
            await WriteSseAsync(response, "completed", new { finishReason = result.FinishReason }, cancellationToken);
            await conversationService.TouchConversationAsync(conversation, cancellationToken);
        }
        catch (Exception ex)
        {
            await conversationService.UpdateMessageAsync(assistant, ex.Message, false, null, null, null, cancellationToken);
            await WriteSseAsync(response, "error", new { message = ex.Message }, cancellationToken);
        }
    }

    private async Task<ExecutionResult> SendMessageWithSessionAsync(Conversation conversation, string content, CancellationToken cancellationToken)
    {
        var agent = conversation.AgentDefinition ?? throw new InvalidOperationException("Conversation agent is missing.");
        var runtime = await modelCatalogService.GetRuntimeOptionsAsync(agent.StudioModelId, cancellationToken);
        var chatClient = providerClientFactory.CreateClient(runtime);
        var session = CreateSession(conversation, agent, runtime.RuntimeModelId, chatClient);

        var response = await session.SendAsync(content, new ChatOptions
        {
            Temperature = agent.Temperature,
            MaxTokens = agent.MaxTokens
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(response.ErrorMessage ?? "Model request failed.");
        }

        return new ExecutionResult(
            response.Message?.TextContent ?? string.Empty,
            response.FinishReason,
            response.Usage?.PromptTokens,
            response.Usage?.CompletionTokens);
    }

    private ChatSession CreateSession(Conversation conversation, AgentDefinition agent, string runtimeModelId, IChatClient chatClient)
    {
        var toolRegistry = toolRegistryFactory.CreateDefaultRegistry();
        var history = new List<ChatMessage>
        {
            ChatMessage.System(BuildSystemPrompt(agent.SystemPrompt))
        };

        foreach (var message in conversation.Messages.OrderBy(x => x.CreatedAtUtc))
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    history.Add(ChatMessage.System(message.Content));
                    break;
                case MessageRole.User:
                    history.Add(ChatMessage.User(message.Content));
                    break;
                case MessageRole.Assistant:
                    history.Add(ChatMessage.Assistant(message.Content));
                    break;
                case MessageRole.Tool:
                    history.Add(new ChatMessage
                    {
                        Role = ChatRole.Tool,
                        ToolCallId = message.Id.ToString(),
                        TextContent = message.Content
                    });
                    break;
            }
        }

        return new ChatSessionBuilder(chatClient, runtimeModelId)
            .WithToolRegistry(toolRegistry)
            .WithHistory(history)
            .Build();
    }

    private static string BuildSystemPrompt(string basePrompt)
        => string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                basePrompt.Trim(),
                "You have access to workspace tools inside the AgileAI repository.",
                "Use list_directory or search_files before guessing file paths, use read_file to inspect text files, and use write_file only when creating or updating workspace files is necessary.",
                "Never claim to have read or written a file unless you actually used the tool."
            }.Where(x => string.IsNullOrWhiteSpace(x) == false));

    private sealed record ExecutionResult(string AssistantContent, string? FinishReason, int? InputTokens, int? OutputTokens);

    private static async Task WriteSseAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
