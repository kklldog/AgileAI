using AgileAI.Extensions.FileSystem;
using AgileAI.Studio.Api.Services;
using AgileAI.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgileAI.Tests;

public class StudioToolRegistryFactoryTests
{
    [Fact]
    public void CreateRegistry_WithEmptyAllowedToolNames_ShouldReturnNoTools()
    {
        var factory = CreateFactory();

        var registry = factory.CreateRegistry([]);

        Assert.Empty(registry.GetToolDefinitions());
    }

    private static StudioToolRegistryFactory CreateFactory()
    {
        var fileSystemOptions = new FileSystemToolOptions
        {
            RootPath = Path.GetTempPath(),
            MaxReadCharacters = 12000
        };
        var pathGuard = new FileSystemPathGuard(fileSystemOptions);
        var webFetchHttpClient = new HttpClient(new FakeHttpMessageHandler((request, ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            })));
        var fileSystemFactory = new FileSystemToolRegistryFactory(
            new ListDirectoryTool(pathGuard),
            new SearchFilesTool(pathGuard),
            new ReadFileTool(pathGuard, fileSystemOptions),
            new ReadFilesBatchTool(pathGuard, fileSystemOptions),
            new WriteFileTool(pathGuard),
            new CreateDirectoryTool(pathGuard),
            new MoveFileTool(pathGuard),
            new PatchFileTool(pathGuard),
            new DeleteFileTool(pathGuard, fileSystemOptions),
            new DeleteDirectoryTool(pathGuard));

        return new StudioToolRegistryFactory(
            fileSystemFactory,
            new RunLocalCommandTool(new AgileAI.Core.ProcessExecutionService()),
            new WebFetchTool(webFetchHttpClient));
    }
}
