namespace AgileAI.Abstractions;

public interface IToolExecutionGate
{
    Task<ToolApprovalDecision> EvaluateAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default);
}
