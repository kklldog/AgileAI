using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.DependencyInjection;
using AgileAI.Extensions.FileSystem;
using AgileAI.Providers.OpenAICompatible.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

var apiKey = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_COMPATIBLE_API_KEY.");
var baseUrl = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_BASE_URL")
    ?? throw new InvalidOperationException("Set OPENAI_COMPATIBLE_BASE_URL.");
var model = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_MODEL")
    ?? throw new InvalidOperationException("Set OPENAI_COMPATIBLE_MODEL.");
var providerName = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_PROVIDER") ?? "openapi";
var rootPath = Environment.GetEnvironmentVariable("AGILEAI_FILETOOLS_ROOT") ?? Directory.GetCurrentDirectory();

services.AddAgileAI();
services.AddOpenAICompatibleProvider(options =>
{
    options.ProviderName = providerName;
    options.ApiKey = apiKey;
    options.BaseUrl = baseUrl;
    options.RelativePath = "chat/completions";
});

var serviceProvider = services.BuildServiceProvider();
var chatClient = serviceProvider.GetRequiredService<IChatClient>();
var toolRegistry = new InMemoryToolRegistry()
    .RegisterFileSystemTools(options =>
    {
        options.RootPath = rootPath;
        options.MaxReadCharacters = 12000;
    });

var session = new ChatSessionBuilder(chatClient, $"{providerName}:{model}")
    .WithToolRegistry(toolRegistry)
    .Build();

var prompt = "Use search_files to find mentions of AgileAI.Studio, then use read_files_batch to inspect the best matching files and summarize them.";
Console.WriteLine($"Root path: {rootPath}");
Console.WriteLine($"Prompt: {prompt}");

var response = await session.SendAsync(prompt, new ChatOptions { Temperature = 0.2, MaxTokens = 500 });

Console.WriteLine();
Console.WriteLine(response.Message?.TextContent);
