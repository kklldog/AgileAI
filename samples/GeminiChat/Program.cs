using AgileAI.Abstractions;
using AgileAI.DependencyInjection;
using AgileAI.Providers.Gemini.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
    ?? throw new InvalidOperationException("Please set GEMINI_API_KEY.");
var model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-2.5-flash";

var services = new ServiceCollection();
services.AddAgileAI();
services.AddGeminiProvider(options =>
{
    options.ApiKey = apiKey;
});

var serviceProvider = services.BuildServiceProvider();
var chatClient = serviceProvider.GetRequiredService<IChatClient>();

var response = await chatClient.CompleteAsync(new ChatRequest
{
    ModelId = $"gemini:{model}",
    Messages = [ChatMessage.User("Say hello from Gemini in one sentence.")]
});

if (!response.IsSuccess)
{
    Console.WriteLine($"Error: {response.ErrorMessage}");
    return;
}

Console.WriteLine(response.Message?.TextContent);
