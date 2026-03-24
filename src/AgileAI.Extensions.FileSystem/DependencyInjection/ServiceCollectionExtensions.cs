using Microsoft.Extensions.DependencyInjection;

namespace AgileAI.Extensions.FileSystem.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgileAIFileSystemTools(this IServiceCollection services, Action<FileSystemToolOptions> configure)
        => services.AddFileSystemTools(configure);

    public static IServiceCollection AddAgileAIFileSystemTools(this IServiceCollection services, FileSystemToolOptions options)
        => services.AddFileSystemTools(options);

    public static IServiceCollection AddFileSystemTools(this IServiceCollection services, Action<FileSystemToolOptions> configure)
    {
        var options = new FileSystemToolOptions();
        configure(options);
        return services.AddFileSystemTools(options);
    }

    public static IServiceCollection AddFileSystemTools(this IServiceCollection services, FileSystemToolOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<FileSystemPathGuard>();
        services.AddScoped<ListDirectoryTool>();
        services.AddScoped<SearchFilesTool>();
        services.AddScoped<ReadFileTool>();
        services.AddScoped<ReadFilesBatchTool>();
        services.AddScoped<WriteFileTool>();
        services.AddScoped<CreateDirectoryTool>();
        services.AddScoped<MoveFileTool>();
        services.AddScoped<PatchFileTool>();
        services.AddScoped<DeleteFileTool>();
        services.AddScoped<DeleteDirectoryTool>();
        services.AddScoped<FileSystemToolRegistryFactory>();
        return services;
    }
}
