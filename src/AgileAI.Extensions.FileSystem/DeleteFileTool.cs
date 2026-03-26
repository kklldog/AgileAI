using System.Text.Json;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public class DeleteFileTool(FileSystemPathGuard pathGuard, FileSystemToolOptions options) : ITool
{
    public string Name => "delete_file";

    public string Description => "Delete a file inside the configured filesystem root. Supports soft delete (recycle bin) with optional force flag.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Root-relative path of the file to delete." },
            force = new { type = "boolean", description = "If true, permanently deletes instead of moving to recycle bin. Default is false (soft delete)." }
        },
        required = new[] { "path" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<DeleteFileRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid delete_file arguments.");

        var resolvedPath = pathGuard.ResolvePath(request.Path);

        if (!File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"File '{request.Path}' does not exist.");
        }

        if (!IsAllowedExtension(resolvedPath))
        {
            throw new InvalidOperationException($"Deletion of '{request.Path}' is not allowed. File extension is not in the whitelist.");
        }

        if (request.Force)
        {
            File.Delete(resolvedPath);
            return new ToolResult
            {
                ToolCallId = context.ToolCall.Id,
                Content = $"Permanently deleted {pathGuard.ToRelativePath(resolvedPath)}.",
                IsSuccess = true
            };
        }

        var recycleBinPath = GetRecycleBinPath(resolvedPath);
        var relativeRecyclePath = pathGuard.ToRelativePath(recycleBinPath);
        File.Move(resolvedPath, recycleBinPath, overwrite: true);

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = $"Moved to Recycle Bin: {pathGuard.ToRelativePath(resolvedPath)} → {relativeRecyclePath}",
            IsSuccess = true
        };
    }

    private bool IsAllowedExtension(string filePath)
    {
        var allowed = options.AllowedDeleteExtensions;
        if (allowed == null || allowed.Length == 0)
        {
            return true;
        }
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return allowed.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private string GetRecycleBinPath(string originalPath)
    {
        var fileName = Path.GetFileName(originalPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var recycleDir = Path.Combine(Path.GetTempPath(), "AgileAI_RecycleBin", timestamp);
        Directory.CreateDirectory(recycleDir);
        return Path.Combine(recycleDir, fileName);
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record DeleteFileRequest(string Path, bool Force = false);
}
