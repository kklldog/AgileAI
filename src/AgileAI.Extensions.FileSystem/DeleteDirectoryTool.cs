using System.Text.Json;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public class DeleteDirectoryTool(FileSystemPathGuard pathGuard, FileSystemToolOptions options) : ITool
{
    public string Name => "delete_directory";

    public string Description => "Delete a directory inside the configured filesystem root. Supports soft delete (recycle bin) with optional force flag.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Root-relative path of the directory to delete." },
            force = new { type = "boolean", description = "If true, permanently deletes instead of moving to recycle bin. Default is false (soft delete)." }
        },
        required = new[] { "path" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<DeleteDirectoryRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid delete_directory arguments.");

        var resolvedPath = pathGuard.ResolvePath(request.Path);

        if (!Directory.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"Directory '{request.Path}' does not exist.");
        }

        if (request.Force)
        {
            Directory.Delete(resolvedPath, recursive: true);
            return new ToolResult
            {
                ToolCallId = context.ToolCall.Id,
                Content = $"Permanently deleted directory {pathGuard.ToRelativePath(resolvedPath)}.",
                IsSuccess = true
            };
        }

        var recycleBinPath = GetRecycleBinPath(resolvedPath);
        var relativeRecyclePath = pathGuard.ToRelativePath(recycleBinPath);
        Directory.Move(resolvedPath, recycleBinPath);

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = $"Moved to recycle bin: {pathGuard.ToRelativePath(resolvedPath)} → {relativeRecyclePath}",
            IsSuccess = true
        };
    }

    private string GetRecycleBinPath(string originalPath)
    {
        var dirName = Path.GetFileName(originalPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var recycleDir = Path.Combine(Path.GetTempPath(), "AgileAI_RecycleBin", timestamp);
        Directory.CreateDirectory(recycleDir);
        return Path.Combine(recycleDir, dirName);
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record DeleteDirectoryRequest(string Path, bool Force = false);
}
