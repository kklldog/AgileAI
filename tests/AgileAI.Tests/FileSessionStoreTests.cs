using AgileAI.Abstractions;
using AgileAI.Core;

namespace AgileAI.Tests;

public class FileSessionStoreTests
{
    [Fact]
    public async Task SaveGetDeleteAsync_ShouldPersistConversationStateAcrossInstances()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var state = new ConversationState
            {
                SessionId = "session-1",
                History = [ChatMessage.User("hi"), ChatMessage.Assistant("hello")],
                ActiveSkill = "weather",
                Metadata = new Dictionary<string, object?>
                {
                    ["source"] = "test"
                },
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var writer = new FileSessionStore(new FileSessionStoreOptions { RootDirectory = rootDirectory });
            await writer.SaveAsync(state);

            var reader = new FileSessionStore(new FileSessionStoreOptions { RootDirectory = rootDirectory });
            var loaded = await reader.GetAsync("session-1");

            Assert.NotNull(loaded);
            Assert.Equal("weather", loaded!.ActiveSkill);
            Assert.Equal(2, loaded.History.Count);
            Assert.Equal("test", loaded.Metadata["source"]?.ToString());

            await reader.DeleteAsync("session-1");
            Assert.Null(await reader.GetAsync("session-1"));
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_WithPathLikeSessionId_ShouldStillPersistSafely()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var store = new FileSessionStore(new FileSessionStoreOptions { RootDirectory = rootDirectory });
            await store.SaveAsync(new ConversationState
            {
                SessionId = "tenant/a/session:b",
                History = [ChatMessage.User("hello")],
                UpdatedAt = DateTimeOffset.UtcNow
            });

            var files = Directory.GetFiles(rootDirectory, "*.json");
            Assert.Single(files);

            var loaded = await store.GetAsync("tenant/a/session:b");
            Assert.NotNull(loaded);
            Assert.Single(loaded!.History);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgileAI.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
