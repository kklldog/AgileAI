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
        var limit = NormalizeLimit(request.MaxCharacters);

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("web_fetch requires a valid absolute URL.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("web_fetch only supports http and https URLs.");
        }

        HttpResponseMessage response;
        string content;
        try
        {
            response = await httpClient.GetAsync(uri, cancellationToken);
            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            return CreateTransportFailureResult(context.ToolCall.Id, uri, "Request timed out.", limit, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return CreateTransportFailureResult(context.ToolCall.Id, uri, "Network request failed.", limit, ex.Message);
        }

        using (response)
        {
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
                        content = Truncate(content, limit)
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
                    content = Truncate(content, limit)
                })
            };
        }
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

    private static ToolResult CreateTransportFailureResult(string toolCallId, Uri uri, string error, int limit, string? detail)
        => new()
        {
            ToolCallId = toolCallId,
            IsSuccess = false,
            Status = ToolExecutionStatus.Failed,
            Content = JsonSerializer.Serialize(new
            {
                url = uri.ToString(),
                error,
                content = Truncate(detail ?? error, limit)
            })
        };

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed record WebFetchRequest(string Url, int? MaxCharacters);
}
