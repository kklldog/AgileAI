using AgileAI.Core;
using AgileAI.Studio.Api.Domain;

namespace AgileAI.Studio.Api.Services;

internal static class DeepSeekProviderOptions
{
    public static object? Build(ProviderConnection? providerConnection, bool includeTools)
    {
        if (includeTools && IsDeepSeekProvider(providerConnection))
        {
            return new OpenAICompatibleProviderRequestOptions
            {
                Thinking = new OpenAICompatibleThinkingOptions { Type = "disabled" }
            };
        }

        return null;
    }

    public static bool IsDeepSeekProvider(ProviderConnection? providerConnection)
    {
        if (providerConnection == null)
        {
            return false;
        }

        if (providerConnection.ProviderType == ProviderType.DeepSeek)
        {
            return true;
        }

        return providerConnection.ProviderType == ProviderType.OpenAICompatible &&
            (ContainsDeepSeek(providerConnection.ProviderName) ||
             ContainsDeepSeek(providerConnection.Name) ||
             ContainsDeepSeek(providerConnection.BaseUrl));
    }

    private static bool ContainsDeepSeek(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
}
