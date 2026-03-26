using System.Text.Json;
using System.Text.Json.Serialization;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public class MoveFileTool(FileSystemPathGuard pathGuard) : ITool
{
    public string Name => "move_file";

    public string Description => "Move or rename a file or directory within the configured filesystem root.";


    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            source_path = new { type = "string", description = "Root-relative path of the file or directory to move." },
            destination_path = new { type = "string", description = "Root-relative destination path. Parent directories are created as needed." }
        },
        required = new[] { "source_path", "destination_path" }
    };

    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<MoveFileRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid move_file arguments.");

        var sourcePath = pathGuard.ResolvePath(request.SourcePath);
        var destPath = pathGuard.ResolvePath(request.DestinationPath);

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Source path '{request.SourcePath}' does not exist.");
        }

        var destDirectory = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrWhiteSpace(destDirectory))
        {
            Directory.CreateDirectory(destDirectory);
        }

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destPath);
        }
        else
        {
            Directory.Move(sourcePath, destPath);
        }

        return Task.FromResult(new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = $"Moved '{pathGuard.ToRelativePath(sourcePath)}' to '{pathGuard.ToRelativePath(destPath)}'.",
            IsSuccess = true
        });
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed class MoveFileRequest
    {
        [JsonPropertyName("source_path")]
        public string SourcePath { get; init; } = string.Empty;

        [JsonPropertyName("destination_path")]
        public string DestinationPath { get; init; } = string.Empty;
    }
}
