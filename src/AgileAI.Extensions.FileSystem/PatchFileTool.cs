using System.Text.Json;
using System.Text.Json.Serialization;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public class PatchFileTool(FileSystemPathGuard pathGuard) : ITool
{
    public string Name => "patch_file";

    public string Description => "Patch (update) an existing file with new content. Creates a backup before modifying. Fails if file doesn't exist unless create_if_missing is true.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Root-relative path of the file to patch." },
            content = new { type = "string", description = "New content to write to the file." },
            create_if_missing = new { type = "boolean", description = "If true, creates the file if it doesn't exist. Default is false." }
        },
        required = new[] { "path", "content" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<PatchFileRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid patch_file arguments.");

        var resolvedPath = pathGuard.ResolvePath(request.Path);

        if (!File.Exists(resolvedPath))
        {
            if (request.CreateIfMissing)
            {
                var directory = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(resolvedPath, request.Content ?? string.Empty, cancellationToken);
                return new ToolResult
                {
                    ToolCallId = context.ToolCall.Id,
                    Content = $"Created and wrote {request.Content?.Length ?? 0} characters to {pathGuard.ToRelativePath(resolvedPath)}.",
                    IsSuccess = true
                };
            }
            throw new InvalidOperationException($"File '{request.Path}' does not exist. Use create_if_missing=true to create it.");
        }

        var oldContent = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        var oldLineCount = oldContent.Split('\n').Length;
        var newLineCount = (request.Content ?? string.Empty).Split('\n').Length;
        var lineDiff = newLineCount - oldLineCount;

        var backupPath = resolvedPath + ".bak";
        await File.WriteAllTextAsync(backupPath, oldContent, cancellationToken);

        await File.WriteAllTextAsync(resolvedPath, request.Content ?? string.Empty, cancellationToken);

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = $"Patched {pathGuard.ToRelativePath(resolvedPath)}. " +
                      $"Lines: {oldLineCount} → {newLineCount} ({lineDiff:+#;-#;0}). " +
                      $"Backup saved to {pathGuard.ToRelativePath(backupPath)}.",
            IsSuccess = true
        };
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed class PatchFileRequest
    {
        public string Path { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;

        [JsonPropertyName("create_if_missing")]
        public bool CreateIfMissing { get; init; }
    }
}
