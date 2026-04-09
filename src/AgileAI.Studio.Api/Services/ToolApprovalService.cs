using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Studio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgileAI.Studio.Api.Services;

public sealed class ToolApprovalService(
    StudioDbContext dbContext,
    ConversationService conversationService,
    AgentService agentService,
    ModelCatalogService modelCatalogService,
    ProviderClientFactory providerClientFactory,
    IServiceProvider serviceProvider,
    StudioToolRegistryFactory toolRegistryFactory,
    StudioStreamingTurnFinalizer streamingTurnFinalizer)
{
    public async Task<ToolApprovalDto> CreatePendingApprovalAsync(
        Conversation conversation,
        Guid assistantMessageId,
        ToolApprovalRequest request,
        string assistantToolCallContent,
        CancellationToken cancellationToken)
    {
        var entity = new ToolApprovalRequestEntity
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            AgentDefinitionId = conversation.AgentDefinitionId,
            AssistantMessageId = assistantMessageId,
            ApprovalRequestId = request.Id,
            ToolCallId = request.ToolCallId,
            ToolName = request.ToolName,
            ArgumentsJson = request.Arguments,
            AssistantToolCallContent = assistantToolCallContent,
            Status = ToolApprovalStatus.Pending,
            RequestedAtUtc = request.RequestedAtUtc
        };

        dbContext.ToolApprovalRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapApproval(entity);
    }

    public async Task<IReadOnlyList<ToolApprovalDto>> GetToolApprovalsAsync(Guid conversationId, CancellationToken cancellationToken)
        => (await dbContext.ToolApprovalRequests
            .Where(x => x.ConversationId == conversationId)
            .ToListAsync(cancellationToken))
            .OrderBy(x => x.RequestedAtUtc.UtcDateTime)
            .Select(MapApproval)
            .ToList();

    public async Task<ToolApprovalResolutionResultDto> ResolveApprovalAsync(Guid approvalId, bool approved, string? comment, CancellationToken cancellationToken)
    {
        var approval = await dbContext.ToolApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken)
            ?? throw new InvalidOperationException("Tool approval request not found.");

        if (approval.Status != ToolApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Tool approval request has already been resolved.");
        }

        var conversation = await conversationService.GetConversationEntityAsync(approval.ConversationId, cancellationToken);
        var assistantMessage = conversation.Messages.FirstOrDefault(x => x.Id == approval.AssistantMessageId)
            ?? throw new InvalidOperationException("Assistant placeholder message not found.");
        var agent = conversation.AgentDefinition ?? throw new InvalidOperationException("Conversation agent is missing.");
        var runtime = await modelCatalogService.GetRuntimeOptionsAsync(agent.StudioModelId, cancellationToken);
        var chatClient = providerClientFactory.CreateClient(runtime);
        var selectedToolNames = await agentService.GetSelectedToolNamesAsync(agent.Id, cancellationToken);
        var toolRegistry = toolRegistryFactory.CreateRegistry(selectedToolNames);
        var chatSession = await CreateSessionAsync(conversation, assistantMessage, approval, agent, runtime.RuntimeModelId, chatClient, cancellationToken);

        var toolResult = await ResolveToolResultAsync(
            approval,
            approved,
            comment,
            toolRegistry,
            chatSession,
            conversation.Id.ToString(),
            cancellationToken);

        chatSession.AddMessage(new ChatMessage { Role = ChatRole.Tool, ToolCallId = approval.ToolCallId, TextContent = toolResult.Content });
        var resumedTurn = await chatSession.ContinueAsync(new ChatOptions
        {
            Temperature = agent.Temperature,
            MaxTokens = agent.MaxTokens
        }, cancellationToken);

        if (!resumedTurn.Response.IsSuccess)
        {
            throw new InvalidOperationException(resumedTurn.Response.ErrorMessage ?? "Failed to resume chat after tool approval.");
        }

        ToolApprovalDto? pendingApprovalDto = null;
        if (resumedTurn.PendingApprovalRequest != null)
        {
            pendingApprovalDto = await CreatePendingApprovalAsync(
                conversation,
                assistantMessage.Id,
                resumedTurn.PendingApprovalRequest,
                resumedTurn.Response.Message?.TextContent ?? string.Empty,
                cancellationToken);
            approval.Status = toolResult.IsSuccess ? ToolApprovalStatus.Completed : ToolApprovalStatus.Failed;
        }
        else
        {
            approval.Status = toolResult.IsSuccess ? ToolApprovalStatus.Completed : ToolApprovalStatus.Failed;
        }

        approval.CompletedAtUtc = DateTimeOffset.UtcNow;

        await conversationService.UpdateMessageAsync(
            assistantMessage,
            resumedTurn.PendingApprovalRequest == null
                ? resumedTurn.Response.Message?.TextContent ?? string.Empty
                : $"Command approval required for {resumedTurn.PendingApprovalRequest.ToolName}.",
            false,
            resumedTurn.Response.FinishReason,
            resumedTurn.Response.Usage?.PromptTokens,
            resumedTurn.Response.Usage?.CompletionTokens,
            assistantMessage.AppliedSkillName,
            null,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await conversationService.TouchConversationAsync(conversation, cancellationToken);

        return new ToolApprovalResolutionResultDto(
            MapApproval(approval),
            ConversationService.MapMessage(assistantMessage),
            await conversationService.MapConversationAsync(conversation, cancellationToken),
            pendingApprovalDto);
    }

    public async Task StreamApprovalResolutionAsync(Guid approvalId, bool approved, string? comment, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        var approval = await dbContext.ToolApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken)
            ?? throw new InvalidOperationException("Tool approval request not found.");

        if (approval.Status != ToolApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Tool approval request has already been resolved.");
        }

        var conversation = await conversationService.GetConversationEntityAsync(approval.ConversationId, cancellationToken);
        var assistantMessage = conversation.Messages.FirstOrDefault(x => x.Id == approval.AssistantMessageId)
            ?? throw new InvalidOperationException("Assistant placeholder message not found.");
        var agent = conversation.AgentDefinition ?? throw new InvalidOperationException("Conversation agent is missing.");
        var runtime = await modelCatalogService.GetRuntimeOptionsAsync(agent.StudioModelId, cancellationToken);
        var chatClient = providerClientFactory.CreateClient(runtime);
        var selectedToolNames = await agentService.GetSelectedToolNamesAsync(agent.Id, cancellationToken);
        var toolRegistry = toolRegistryFactory.CreateRegistry(selectedToolNames);
        var chatSession = await CreateSessionAsync(conversation, assistantMessage, approval, agent, runtime.RuntimeModelId, chatClient, cancellationToken);

        var toolResult = await ResolveToolResultAsync(
            approval,
            approved,
            comment,
            toolRegistry,
            chatSession,
            conversation.Id.ToString(),
            cancellationToken);

        chatSession.AddMessage(new ChatMessage { Role = ChatRole.Tool, ToolCallId = approval.ToolCallId, TextContent = toolResult.Content });

        await foreach (var update in chatSession.ContinueStreamAsync(new ChatOptions
        {
            Temperature = agent.Temperature,
            MaxTokens = agent.MaxTokens
        }, cancellationToken))
        {
            switch (update)
            {
                case ChatTurnTextDelta textDelta:
                    await StudioSseWriter.WriteAsync(response, "text-delta", new { delta = textDelta.Delta }, cancellationToken);
                    break;
                case ChatTurnUsage usage:
                    await StudioSseWriter.WriteAsync(response, "usage", new
                    {
                        inputTokens = usage.Usage.PromptTokens,
                        outputTokens = usage.Usage.CompletionTokens
                    }, cancellationToken);
                    break;
                case ChatTurnPendingApproval pendingApproval:
                {
                    var pendingApprovalDto = await CreatePendingApprovalAsync(
                        conversation,
                        assistantMessage.Id,
                        pendingApproval.PendingApprovalRequest,
                        pendingApproval.Response.Message?.TextContent ?? string.Empty,
                        cancellationToken);

                    approval.Status = toolResult.IsSuccess ? ToolApprovalStatus.Completed : ToolApprovalStatus.Failed;
                    approval.CompletedAtUtc = DateTimeOffset.UtcNow;

                    var waitingContent = $"Command approval required for {pendingApproval.PendingApprovalRequest.ToolName}.";
                    await streamingTurnFinalizer.FinalizePendingApprovalAsync(
                        conversation,
                        assistantMessage,
                        response,
                        waitingContent,
                        pendingApprovalDto,
                        assistantMessage.AppliedSkillName,
                        pendingApproval.ToolNames,
                        async ct => await dbContext.SaveChangesAsync(ct),
                        cancellationToken);
                    return;
                }
                case ChatTurnCompleted completed:
                {
                    if (!completed.Response.IsSuccess)
                    {
                        throw new InvalidOperationException(completed.Response.ErrorMessage ?? "Failed to resume chat after tool approval.");
                    }

                    approval.Status = toolResult.IsSuccess ? ToolApprovalStatus.Completed : ToolApprovalStatus.Failed;
                    approval.CompletedAtUtc = DateTimeOffset.UtcNow;

                    var finalContent = completed.Response.Message?.TextContent ?? string.Empty;
                    await streamingTurnFinalizer.FinalizeCompletedAsync(
                        conversation,
                        assistantMessage,
                        response,
                        finalContent,
                        completed.Response.FinishReason,
                        completed.Response.Usage?.PromptTokens,
                        completed.Response.Usage?.CompletionTokens,
                        assistantMessage.AppliedSkillName,
                        completed.ToolNames,
                        async ct => await dbContext.SaveChangesAsync(ct),
                        cancellationToken);
                    return;
                }
                case ChatTurnError error:
                    throw new InvalidOperationException(error.ErrorMessage);
            }
        }

        throw new InvalidOperationException("Approval resume stream ended without a terminal update.");
    }

    private static IReadOnlyList<ChatMessage> BuildResumeHistory(Conversation conversation, Guid assistantPlaceholderId, ToolApprovalRequestEntity approval)
    {
        var history = new List<ChatMessage>
        {
            ChatMessage.System(AgentExecutionService.BuildSystemPrompt(conversation.AgentDefinition?.SystemPrompt ?? string.Empty))
        };

        foreach (var message in conversation.Messages.OrderBy(x => x.CreatedAtUtc))
        {
            if (message.Id == assistantPlaceholderId)
            {
                continue;
            }

            history.Add(message.Role switch
            {
                MessageRole.System => ChatMessage.System(message.Content),
                MessageRole.User => ChatMessage.User(message.Content),
                MessageRole.Assistant => ChatMessage.Assistant(message.Content),
                MessageRole.Tool => new ChatMessage { Role = ChatRole.Tool, ToolCallId = message.Id.ToString(), TextContent = message.Content },
                _ => ChatMessage.User(message.Content)
            });
        }

        history.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            TextContent = approval.AssistantToolCallContent,
            ToolCalls =
            [
                new ToolCall
                {
                    Id = approval.ToolCallId,
                    Name = approval.ToolName,
                    Arguments = approval.ArgumentsJson
                }
            ]
        });

        return history;
    }

    private static ToolApprovalDto MapApproval(ToolApprovalRequestEntity entity)
        => new(
            entity.Id,
            entity.ConversationId,
            entity.AssistantMessageId,
            entity.ApprovalRequestId,
            entity.ToolCallId,
            entity.ToolName,
            entity.ArgumentsJson,
            entity.Status.ToString(),
            entity.DecisionComment,
            entity.ResultContent,
            entity.ExitCode,
            entity.StandardOutput,
            entity.StandardError,
            entity.RequestedAtUtc,
            entity.DecidedAtUtc,
            entity.CompletedAtUtc);

    private static async Task<ToolResult> ResolveToolResultAsync(
        ToolApprovalRequestEntity approval,
        bool approved,
        string? comment,
        IToolRegistry toolRegistry,
        ChatSession chatSession,
        string conversationId,
        CancellationToken cancellationToken)
    {
        approval.DecisionComment = comment;
        approval.DecidedAtUtc = DateTimeOffset.UtcNow;

        ToolResult toolResult;
        if (approved)
        {
            approval.Status = ToolApprovalStatus.Approved;
            if (!toolRegistry.TryGetTool(approval.ToolName, out var tool) || tool == null)
            {
                throw new InvalidOperationException($"Tool '{approval.ToolName}' not found.");
            }

            var toolCall = new ToolCall { Id = approval.ToolCallId, Name = approval.ToolName, Arguments = approval.ArgumentsJson };
            toolResult = await tool.ExecuteAsync(new ToolExecutionContext
            {
                ToolCall = toolCall,
                ChatHistory = chatSession.History,
                ConversationId = conversationId,
                ServiceProvider = null
            }, cancellationToken);
        }
        else
        {
            approval.Status = ToolApprovalStatus.Denied;
            toolResult = new ToolResult
            {
                ToolCallId = approval.ToolCallId,
                IsSuccess = false,
                Status = ToolExecutionStatus.Denied,
                Content = comment ?? $"Execution of tool '{approval.ToolName}' was denied by the user."
            };
        }

        approval.ResultContent = toolResult.Content;
        if (toolResult.Data is ProcessExecutionResult processResult)
        {
            approval.ExitCode = processResult.ExitCode;
            approval.StandardOutput = processResult.StandardOutput;
            approval.StandardError = processResult.StandardError;
        }

        return toolResult;
    }

    private Task<ChatSession> CreateSessionAsync(
        Conversation conversation,
        ConversationMessage assistantMessage,
        ToolApprovalRequestEntity approval,
        AgentDefinition agent,
        string runtimeModelId,
        IChatClient chatClient,
        CancellationToken cancellationToken)
        => StudioChatSessionFactory.CreateAsync(
            agentService,
            toolRegistryFactory,
            new StudioToolExecutionGate(),
            serviceProvider,
            conversation,
            agent,
            runtimeModelId,
            chatClient,
            BuildResumeHistory(conversation, assistantMessage.Id, approval),
            cancellationToken);

}
