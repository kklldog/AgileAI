using AgileAI.Studio.Api.Domain;
using AgileAI.Studio.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgileAI.Tests;

public class ProviderClientFactoryTests
{
    [Fact]
    public async Task CreateClient_WithDemoLocalOpenAI_ShouldUseMockProvider()
    {
        var factory = new ProviderClientFactory(NullLoggerFactory.Instance);

        var client = factory.CreateClient(new ProviderRuntimeOptions(
            ProviderType.OpenAI,
            "openai",
            "openai:gpt-4o-mini",
            "demo-local",
            "mock://studio/v1/",
            null,
            null,
            null,
            null,
            null));

        var response = await client.CompleteAsync(new AgileAI.Abstractions.ChatRequest
        {
            ModelId = "openai:gpt-4o-mini",
            Messages = [AgileAI.Abstractions.ChatMessage.User("Respond with OK")]
        });

        Assert.True(response.IsSuccess);
        Assert.Equal("OK", response.Message?.TextContent);
    }

    [Fact]
    public async Task CreateClient_WithDemoLocalAzureAndCustomRuntimeName_ShouldRegisterCanonicalAlias()
    {
        var factory = new ProviderClientFactory(NullLoggerFactory.Instance);

        var client = factory.CreateClient(new ProviderRuntimeOptions(
            ProviderType.AzureOpenAI,
            "my-azure",
            "my-azure:deployment-1",
            "demo-local",
            null,
            "mock://azure.example/",
            null,
            null,
            null,
            "2024-02-01"));

        var response = await client.CompleteAsync(new AgileAI.Abstractions.ChatRequest
        {
            ModelId = "azure-openai:deployment-1",
            Messages = [AgileAI.Abstractions.ChatMessage.User("Respond with OK")]
        });

        Assert.True(response.IsSuccess);
        Assert.Equal("OK", response.Message?.TextContent);
    }

    [Fact]
    public void CreateClient_WithRealOpenAIOptions_ShouldConstructClient()
    {
        var factory = new ProviderClientFactory(NullLoggerFactory.Instance);

        var client = factory.CreateClient(new ProviderRuntimeOptions(
            ProviderType.OpenAI,
            "openai",
            "openai:gpt-4o",
            "real-key",
            "https://api.openai.com/v1/",
            null,
            null,
            null,
            null,
            null));

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_WithRealOpenAICompatibleOptions_ShouldConstructClient()
    {
        var factory = new ProviderClientFactory(NullLoggerFactory.Instance);

        var client = factory.CreateClient(new ProviderRuntimeOptions(
            ProviderType.OpenAICompatible,
            "gateway",
            "gateway:model-a",
            "real-key",
            "https://gateway.example/v1/",
            null,
            "chat/completions",
            AgileAI.Providers.OpenAICompatible.OpenAICompatibleAuthMode.ApiKeyHeader,
            "x-api-key",
            null));

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_WithRealAzureOptions_ShouldConstructClient()
    {
        var factory = new ProviderClientFactory(NullLoggerFactory.Instance);

        var client = factory.CreateClient(new ProviderRuntimeOptions(
            ProviderType.AzureOpenAI,
            "azure-openai",
            "azure-openai:deployment-a",
            "real-key",
            null,
            "https://example-resource.openai.azure.com/",
            null,
            null,
            null,
            "2024-02-01"));

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_WithUnsupportedProviderType_ShouldThrow()
    {
        var factory = new ProviderClientFactory(NullLoggerFactory.Instance);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient(new ProviderRuntimeOptions(
            (ProviderType)999,
            "unknown",
            "unknown:model",
            "key",
            null,
            null,
            null,
            null,
            null,
            null)));

        Assert.Equal("Unsupported provider type.", error.Message);
    }
}
