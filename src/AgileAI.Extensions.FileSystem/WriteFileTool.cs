using System.Text.Json;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

[NeedApproval]
public class WriteFileTool(FileSystemPathGuard pathGuard) : ITool
{
    public string Name => "write_file";

    public string Description => "Write a text file inside the configured filesystem root.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Root-relative file path to write." },
            content = new { type = "string", description = "Text content that should be written to the file." }
        },
        required = new[] { "path", "content" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<WriteFileRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid write_file arguments.");

        var resolvedPath = pathGuard.ResolvePath(request.Path);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(resolvedPath, request.Content ?? string.Empty, cancellationToken);
        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = $"Wrote {request.Content?.Length ?? 0} characters to {pathGuard.ToRelativePath(resolvedPath)}.",
            IsSuccess = true
        };
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record WriteFileRequest(string Path, string Content);
}
