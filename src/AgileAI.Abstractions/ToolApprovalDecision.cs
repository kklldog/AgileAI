namespace AgileAI.Abstractions;

public record ToolApprovalDecision
{
    public string ApprovalRequestId { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public bool IsPending { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset DecidedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static ToolApprovalDecision ApprovedDecision(string approvalRequestId, string? comment = null)
        => new()
        {
            ApprovalRequestId = approvalRequestId,
            Approved = true,
            Comment = comment
        };

    public static ToolApprovalDecision DeniedDecision(string approvalRequestId, string? comment = null)
        => new()
        {
            ApprovalRequestId = approvalRequestId,
            Approved = false,
            Comment = comment
        };

    public static ToolApprovalDecision PendingDecision(string approvalRequestId, string? comment = null)
        => new()
        {
            ApprovalRequestId = approvalRequestId,
            IsPending = true,
            Comment = comment
        };
}
