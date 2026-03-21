using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgileAI(this IServiceCollection services)
    {
        services.AddSingleton<ChatClient>(sp =>
        {
            var chatClient = new ChatClient(sp.GetService<Microsoft.Extensions.Logging.ILogger<ChatClient>>());
            var providers = sp.GetServices<IChatModelProvider>();
            foreach (var provider in providers)
            {
                chatClient.RegisterProvider(provider);
            }
            return chatClient;
        });
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<ChatClient>());
        services.AddSingleton<IToolRegistry, InMemoryToolRegistry>();
        services.AddSingleton<ISkillRegistry, InMemorySkillRegistry>();
        services.AddSingleton<ISkillPlanner, RuleBasedSkillPlanner>();
        services.AddSingleton<ISkillContinuationPolicy, DefaultSkillContinuationPolicy>();
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<ISkillExecutor, PromptSkillExecutor>();
        services.AddSingleton<ILocalSkillLoader, LocalFileSkillLoader>();
        services.AddSingleton<IAgentRuntime, DefaultAgentRuntime>();

        return services;
    }

    public static IServiceCollection AddChatClientProvider(this IServiceCollection services, IChatModelProvider provider)
    {
        services.AddSingleton(provider);
        return services;
    }

    public static IServiceCollection AddLocalSkills(this IServiceCollection services, Action<LocalSkillsOptions>? configure = null)
    {
        var options = new LocalSkillsOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddSingleton<ISkillRegistry>(sp =>
        {
            var registry = new InMemorySkillRegistry();
            var loader = new LocalFileSkillLoader(options);
            var executor = sp.GetRequiredService<ISkillExecutor>();
            var manifests = loader.LoadFromDirectoryAsync(options.RootDirectory).GetAwaiter().GetResult();
            registry.Register(manifests.Select(m => (ISkill)new LocalFileSkill(m, executor)));
            return registry;
        });

        return services;
    }

    public static IServiceCollection AddFileSessionStore(this IServiceCollection services, Action<FileSessionStoreOptions>? configure = null)
    {
        var options = new FileSessionStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.RemoveAll<ISessionStore>();
        services.AddSingleton<ISessionStore>(sp => new FileSessionStore(
            sp.GetRequiredService<FileSessionStoreOptions>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<FileSessionStore>>()));

        return services;
    }
}
