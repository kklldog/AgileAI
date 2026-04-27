namespace AgileAI.Abstractions;

public abstract record StreamingChatUpdate;

public record TextDeltaUpdate(string Delta) : StreamingChatUpdate;

public record ReasoningDeltaUpdate(string Delta) : StreamingChatUpdate;

public record ToolCallDeltaUpdate(string ToolCallId, string? NameDelta, string? ArgumentsDelta) : StreamingChatUpdate;

public record UsageUpdate(UsageInfo Usage) : StreamingChatUpdate;

public record CompletedUpdate(string? FinishReason) : StreamingChatUpdate;

public record ErrorUpdate(string ErrorMessage) : StreamingChatUpdate;
