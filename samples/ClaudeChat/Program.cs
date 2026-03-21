using AgileAI.Abstractions;
using AgileAI.DependencyInjection;
using AgileAI.Providers.Claude.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var apiKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY")
    ?? throw new InvalidOperationException("Please set CLAUDE_API_KEY.");
var model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-3-5-sonnet-latest";
var version = Environment.GetEnvironmentVariable("CLAUDE_API_VERSION") ?? "2023-06-01";

var services = new ServiceCollection();
services.AddAgileAI();
services.AddClaudeProvider(options =>
{
    options.ApiKey = apiKey;
    options.Version = version;
});

var serviceProvider = services.BuildServiceProvider();
var chatClient = serviceProvider.GetRequiredService<IChatClient>();

var response = await chatClient.CompleteAsync(new ChatRequest
{
    ModelId = $"claude:{model}",
    Messages = [ChatMessage.User("Say hello from Claude in one sentence.")]
});

if (!response.IsSuccess)
{
    Console.WriteLine($"Error: {response.ErrorMessage}");
    return;
}

Console.WriteLine(response.Message?.TextContent);
