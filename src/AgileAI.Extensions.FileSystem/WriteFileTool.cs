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
        var request = ParseRequest(context.ToolCall.Arguments);

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

    private static WriteFileRequest ParseRequest(string arguments)
    {
        try
        {
            return JsonSerializer.Deserialize<WriteFileRequest>(arguments, JsonOptions())
                ?? throw new InvalidOperationException("Invalid write_file arguments.");
        }
        catch (JsonException ex)
        {
            var path = ExtractJsonString(arguments, "path");
            var content = ExtractJsonString(arguments, "content", allowUnterminated: true);
            if (string.IsNullOrWhiteSpace(path) || content == null)
            {
                throw new InvalidOperationException("Invalid write_file arguments.", ex);
            }

            return new WriteFileRequest(path, content);
        }
    }

    private static string? ExtractJsonString(string json, string propertyName, bool allowUnterminated = false)
    {
        var propertyToken = $"\"{propertyName}\"";
        var propertyIndex = json.IndexOf(propertyToken, StringComparison.OrdinalIgnoreCase);
        if (propertyIndex < 0)
        {
            return null;
        }

        var colonIndex = json.IndexOf(':', propertyIndex + propertyToken.Length);
        if (colonIndex < 0)
        {
            return null;
        }

        var startQuoteIndex = json.IndexOf('"', colonIndex + 1);
        if (startQuoteIndex < 0)
        {
            return null;
        }

        var value = new System.Text.StringBuilder();
        var escaped = false;
        for (var index = startQuoteIndex + 1; index < json.Length; index++)
        {
            var current = json[index];
            if (escaped)
            {
                value.Append(current switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => current
                });
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                return value.ToString();
            }

            value.Append(current);
        }

        return allowUnterminated ? value.ToString() : null;
    }

    private sealed record WriteFileRequest(string Path, string Content);
}
