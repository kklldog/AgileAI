using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Core;

public sealed class ToolExecutor
{
    private readonly IToolExecutionGate _executionGate;
    private readonly ILogger<ToolExecutor>? _logger;
    private readonly IReadOnlyList<IToolExecutionMiddleware> _middlewares;

    public ToolExecutor(
        IToolExecutionGate executionGate,
        ILogger<ToolExecutor>? logger = null,
        IEnumerable<IToolExecutionMiddleware>? toolExecutionMiddlewares = null)
    {
        _executionGate = executionGate;
        _logger = logger;
        _middlewares = toolExecutionMiddlewares?.ToList() ?? [];
    }

    public async Task<(ToolResult Result, ToolApprovalRequest? PendingApprovalRequest)> ExecuteAsync(
        ITool tool,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var middlewareContext = new ToolExecutionMiddlewareContext
        {
            Tool = tool,
            ExecutionContext = context,
            ServiceProvider = context.ServiceProvider
        };

        var outcome = await MiddlewarePipeline.ExecuteAsync(
            _middlewares,
            middlewareContext,
            static (middleware, executionContext, next, ct) => middleware.InvokeAsync(executionContext, next, ct),
            () => ExecuteCoreAsync(tool, context, cancellationToken),
            cancellationToken);

        return (outcome.Result, outcome.PendingApprovalRequest);
    }

    private async Task<ToolExecutionOutcome> ExecuteCoreAsync(
        ITool tool,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ToolApprovalRequest? approvalRequest = null;
        var approvalMode = ToolApprovalMetadataResolver.ResolveApprovalMode(tool);

        if (approvalMode == ToolApprovalMode.PerExecution)
        {
            approvalRequest = new ToolApprovalRequest
            {
                ToolCallId = context.ToolCall.Id,
                ToolName = tool.Name,
                Arguments = context.ToolCall.Arguments,
                SessionId = context.SessionId,
                ConversationId = context.ConversationId,
                ChatHistory = context.ChatHistory
            };

            var decision = await _executionGate.EvaluateAsync(approvalRequest, cancellationToken);
            if (decision.IsPending)
            {
                return new ToolExecutionOutcome
                {
                    Result = new ToolResult
                    {
                        ToolCallId = context.ToolCall.Id,
                        Content = decision.Comment ?? $"Execution of tool '{tool.Name}' is waiting for approval.",
                        IsSuccess = false,
                        Status = ToolExecutionStatus.AwaitingApproval,
                        ApprovalRequestId = approvalRequest.Id
                    },
                    PendingApprovalRequest = approvalRequest
                };
            }

            if (!decision.Approved)
            {
                return new ToolExecutionOutcome
                {
                    Result = new ToolResult
                    {
                        ToolCallId = context.ToolCall.Id,
                        Content = decision.Comment ?? $"Execution of tool '{tool.Name}' was denied.",
                        IsSuccess = false,
                        Status = ToolExecutionStatus.Denied,
                        ApprovalRequestId = approvalRequest.Id
                    }
                };
            }
        }

        try
        {
            var result = await tool.ExecuteAsync(context, cancellationToken);
            return new ToolExecutionOutcome
            {
                Result = approvalRequest == null ? result : result with { ApprovalRequestId = approvalRequest.Id }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing tool '{ToolName}'", tool.Name);
            return new ToolExecutionOutcome
            {
                Result = new ToolResult
                {
                    ToolCallId = context.ToolCall.Id,
                    Content = $"Error executing tool '{tool.Name}': {ex.Message}",
                    IsSuccess = false,
                    Status = ToolExecutionStatus.Failed,
                    ApprovalRequestId = approvalRequest?.Id
                }
            };
        }
    }
}
