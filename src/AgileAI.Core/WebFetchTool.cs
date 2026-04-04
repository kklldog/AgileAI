using System.Text.Json;
using AgileAI.Abstractions;

namespace AgileAI.Core;

public sealed class WebFetchTool(HttpClient httpClient) : ITool
{
    public string Name => "web_fetch";
    public string Description => "Fetch webpage content from a URL using HTTP GET.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            url = new { type = "string", description = "Absolute HTTP or HTTPS URL to fetch." },
            maxCharacters = new { type = "integer", description = "Optional maximum response characters to return.", minimum = 256, maximum = 50000 }
        },
        required = new[] { "url" }
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<WebFetchRequest>(context.ToolCall.Arguments, JsonOptions())
            ?? throw new InvalidOperationException("Invalid web_fetch arguments.");

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("web_fetch requires a valid absolute URL.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("web_fetch only supports http and https URLs.");
        }

        using var response = await httpClient.GetAsync(uri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ToolResult
            {
                ToolCallId = context.ToolCall.Id,
                IsSuccess = false,
                Status = ToolExecutionStatus.Failed,
                Content = JsonSerializer.Serialize(new
                {
                    url = uri.ToString(),
                    statusCode = (int)response.StatusCode,
                    reasonPhrase = response.ReasonPhrase,
                    content = Truncate(content, NormalizeLimit(request.MaxCharacters))
                })
            };
        }

        return new ToolResult
        {
            ToolCallId = context.ToolCall.Id,
            IsSuccess = true,
            Status = ToolExecutionStatus.Completed,
            Content = JsonSerializer.Serialize(new
            {
                url = uri.ToString(),
                statusCode = (int)response.StatusCode,
                contentType = response.Content.Headers.ContentType?.ToString(),
                content = Truncate(content, NormalizeLimit(request.MaxCharacters))
            })
        };
    }

    private static int NormalizeLimit(int? requested)
        => requested is >= 256 and <= 50000 ? requested.Value : 12000;

    private static string Truncate(string content, int limit)
    {
        if (content.Length <= limit)
        {
            return content;
        }

        return $"{content[..limit]}\n\n[Output truncated to {limit} characters]";
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record WebFetchRequest(string Url, int? MaxCharacters);
}
