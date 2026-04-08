using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Studio.Api.Domain;

namespace AgileAI.Studio.Api.Services;

internal static class StudioChatSessionFactory
{
    private const int StudioMaxToolLoopIterations = 12;

    public static async Task<ChatSession> CreateAsync(
        AgentService agentService,
        StudioToolRegistryFactory toolRegistryFactory,
        StudioToolExecutionGate toolExecutionGate,
        IServiceProvider serviceProvider,
        Conversation conversation,
        AgentDefinition agent,
        string runtimeModelId,
        IChatClient chatClient,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        var selectedToolNames = await agentService.GetSelectedToolNamesAsync(agent.Id, cancellationToken);
        var toolRegistry = toolRegistryFactory.CreateRegistry(selectedToolNames);

        return new ChatSessionBuilder(chatClient, runtimeModelId)
            .WithToolRegistry(toolRegistry)
            .WithMaxToolLoopIterations(StudioMaxToolLoopIterations)
            .WithToolExecutionGate(toolExecutionGate)
            .UseServiceProvider(serviceProvider)
            .WithConversationId(conversation.Id.ToString())
            .WithHistory(history)
            .Build();
    }
}
