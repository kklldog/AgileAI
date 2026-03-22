using System.Text.Json;
using AgileAI.Abstractions;

namespace AgileAI.Extensions.FileSystem;

public class SearchFilesTool(FileSystemPathGuard pathGuard) : ITool
{
    public string Name => "search_files";

    public string Description => "Search text files under the configured filesystem root for a keyword or phrase.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Root-relative directory to search from. Use . for the configured root." },
            query = new { type = "string", description = "Case-insensitive text to search for." },
            limit = new { type = "integer", description = "Maximum number of matching files to return.", minimum = 1, maximum = 50 }
        },
        required = new[] { "path", "query" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<SearchFilesRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid search_files arguments.");

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new InvalidOperationException("search_files requires a non-empty query.");
        }

        var resolvedPath = pathGuard.ResolvePath(request.Path);
        if (!Directory.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"Directory '{request.Path}' was not found.");
        }

        var limit = request.Limit is > 0 and <= 50 ? request.Limit.Value : 10;
        var results = new List<string>();
        foreach (var file in Directory.EnumerateFiles(resolvedPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch
            {
                continue;
            }

            if (!content.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(pathGuard.ToRelativePath(file));
            if (results.Count >= limit)
            {
                break;
            }
        }

        var output = results.Count == 0
            ? $"No files under {pathGuard.ToRelativePath(resolvedPath)} matched '{request.Query}'."
            : string.Join(Environment.NewLine, results);

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            Content = output,
            IsSuccess = true
        };
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record SearchFilesRequest(string Path, string Query, int? Limit);
}
