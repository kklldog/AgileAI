namespace AgileAI.Core;

public class FileSessionStoreOptions
{
    public string RootDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "agileai-sessions");
}
