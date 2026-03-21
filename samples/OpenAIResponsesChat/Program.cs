using AgileAI.Abstractions;
using AgileAI.DependencyInjection;
using AgileAI.Providers.OpenAIResponses.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Please set OPENAI_API_KEY.");
var model = Environment.GetEnvironmentVariable("OPENAI_RESPONSES_MODEL") ?? "gpt-4.1-mini";

var services = new ServiceCollection();
services.AddAgileAI();
services.AddOpenAIResponsesProvider(options =>
{
    options.ApiKey = apiKey;
});

var serviceProvider = services.BuildServiceProvider();
var chatClient = serviceProvider.GetRequiredService<IChatClient>();

var response = await chatClient.CompleteAsync(new ChatRequest
{
    ModelId = $"openai-responses:{model}",
    Messages = [ChatMessage.User("Say hello from the OpenAI Responses API in one sentence.")]
});

if (!response.IsSuccess)
{
    Console.WriteLine($"Error: {response.ErrorMessage}");
    return;
}

Console.WriteLine(response.Message?.TextContent);
