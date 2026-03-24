using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public class ReadFilesBatchTool(FileSystemPathGuard pathGuard, FileSystemToolOptions options) : ITool
{
    public string Name => "read_files_batch";

    public string Description => "Read multiple text files inside the configured filesystem root in a single tool call.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            paths = new
            {
                type = "array",
                description = "Root-relative file paths to read.",
                items = new { type = "string" }
            }
        },
        required = new[] { "paths" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<ReadFilesBatchRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid read_files_batch arguments.");

        if (request.Paths is null || request.Paths.Count == 0)
        {
            throw new InvalidOperationException("read_files_batch requires at least one file path.");
        }

        var builder = new StringBuilder();
        foreach (var path in request.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolvedPath = pathGuard.ResolvePath(path);
            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException($"File '{path}' was not found.");
            }

            var content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
            builder.AppendLine($"Path: {pathGuard.ToRelativePath(resolvedPath)}");
            builder.AppendLine();
            if (content.Length > options.MaxReadCharacters)
            {
                builder.Append(content[..options.MaxReadCharacters]);
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine($"[Output truncated to {options.MaxReadCharacters} characters]");
            }
            else
            {
                builder.AppendLine(content);
            }

            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = builder.ToString().TrimEnd(),
            IsSuccess = true
        };
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record ReadFilesBatchRequest(IReadOnlyList<string> Paths);
}
