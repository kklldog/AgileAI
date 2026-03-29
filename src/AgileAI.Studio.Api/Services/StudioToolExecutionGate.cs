using AgileAI.Abstractions;
namespace AgileAI.Studio.Api.Services;

public sealed class StudioToolExecutionGate : IToolExecutionGate
{
    public async Task<ToolApprovalDecision> EvaluateAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        return ToolApprovalDecision.PendingDecision(request.Id, $"Execution of tool '{request.ToolName}' is waiting for approval.");
    }
}
