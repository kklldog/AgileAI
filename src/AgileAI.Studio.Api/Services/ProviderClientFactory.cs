using AgileAI.Abstractions;
using AgileAI.Core;
using AgileAI.Studio.Api.Domain;
using AgileAI.Providers.AzureOpenAI;
using AgileAI.Providers.OpenAI;
using AgileAI.Providers.OpenAICompatible;
using Microsoft.Extensions.Logging;

namespace AgileAI.Studio.Api.Services;

public class ProviderClientFactory(ILoggerFactory loggerFactory)
{
    public IChatClient CreateClient(ProviderRuntimeOptions options)
    {
        var chatClient = new ChatClient(loggerFactory.CreateLogger<ChatClient>());

        if (ShouldUseMockProvider(options))
        {
            var canonicalProviderName = options.ProviderType switch
            {
                ProviderType.OpenAI => "openai",
                ProviderType.AzureOpenAI => "azure-openai",
                ProviderType.DeepSeek => "deepseek",
                _ => options.RuntimeProviderName
            };

            chatClient.RegisterProvider(new MockChatModelProvider(options.RuntimeProviderName));
            if (!string.Equals(options.RuntimeProviderName, canonicalProviderName, StringComparison.OrdinalIgnoreCase))
            {
                chatClient.RegisterProvider(new MockChatModelProvider(canonicalProviderName));
            }

            return chatClient;
        }

        IChatModelProvider provider = options.ProviderType switch
        {
            ProviderType.OpenAI => new OpenAIChatModelProvider(
                CreateHttpClient(options.BaseUrl),
                new OpenAIOptions
                {
                    ApiKey = options.ApiKey,
                    BaseUrl = options.BaseUrl
                },
                loggerFactory.CreateLogger<OpenAIChatModelProvider>()),
            ProviderType.OpenAICompatible => new OpenAICompatibleChatModelProvider(
                CreateHttpClient(options.BaseUrl),
                new OpenAICompatibleOptions
                {
                    ProviderName = options.RuntimeProviderName,
                    ApiKey = options.ApiKey,
                    BaseUrl = options.BaseUrl ?? string.Empty,
                    RelativePath = options.RelativePath ?? "chat/completions",
                    AuthMode = options.AuthMode ?? OpenAICompatibleAuthMode.Bearer,
                    ApiKeyHeaderName = options.ApiKeyHeaderName
                },
                loggerFactory.CreateLogger<OpenAICompatibleChatModelProvider>()),
            ProviderType.DeepSeek => new OpenAICompatibleChatModelProvider(
                CreateHttpClient(options.BaseUrl),
                new OpenAICompatibleOptions
                {
                    ProviderName = options.RuntimeProviderName,
                    ApiKey = options.ApiKey,
                    BaseUrl = options.BaseUrl ?? string.Empty,
                    RelativePath = options.RelativePath ?? "chat/completions",
                    AuthMode = options.AuthMode ?? OpenAICompatibleAuthMode.Bearer,
                    ApiKeyHeaderName = options.ApiKeyHeaderName
                },
                loggerFactory.CreateLogger<OpenAICompatibleChatModelProvider>()),
            ProviderType.AzureOpenAI => new AzureOpenAIChatModelProvider(
                CreateHttpClient(options.Endpoint),
                new AzureOpenAIOptions
                {
                    ApiKey = options.ApiKey,
                    Endpoint = options.Endpoint ?? string.Empty,
                    ApiVersion = options.ApiVersion ?? "2024-02-01"
                },
                loggerFactory.CreateLogger<AzureOpenAIChatModelProvider>()),
            _ => throw new InvalidOperationException("Unsupported provider type.")
        };

        chatClient.RegisterProvider(provider);
        return chatClient;
    }

    private static HttpClient CreateHttpClient(string? baseAddress)
    {
        var client = new HttpClient();
        if (!string.IsNullOrWhiteSpace(baseAddress))
        {
            client.BaseAddress = new Uri(baseAddress);
        }

        return client;
    }

    private static bool ShouldUseMockProvider(ProviderRuntimeOptions options)
    {
        if (string.Equals(options.ApiKey, "demo-local", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.ApiKey, "replace-with-real-key", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return StartsWithMockScheme(options.BaseUrl) || StartsWithMockScheme(options.Endpoint);
    }

    private static bool StartsWithMockScheme(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith("mock://", StringComparison.OrdinalIgnoreCase);
}
