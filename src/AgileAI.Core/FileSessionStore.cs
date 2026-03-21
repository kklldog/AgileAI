using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgileAI.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgileAI.Core;

public class FileSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly ILogger<FileSessionStore>? _logger;

    public FileSessionStore(FileSessionStoreOptions options, ILogger<FileSessionStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new ArgumentException("RootDirectory is required", nameof(options));
        }

        _rootDirectory = options.RootDirectory;
        _logger = logger;
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<ConversationState?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);

        var filePath = GetFilePath(sessionId);
        if (!File.Exists(filePath))
        {
            _logger?.LogDebug("FileSessionStore GetAsync. SessionId={SessionId}, Found=false", sessionId);
            return null;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(filePath);
            var state = await JsonSerializer.DeserializeAsync<ConversationState>(stream, JsonOptions, cancellationToken);
            _logger?.LogDebug(
                "FileSessionStore GetAsync. SessionId={SessionId}, Found={Found}, HistoryCount={HistoryCount}, ActiveSkill={ActiveSkill}",
                sessionId,
                state != null,
                state?.History?.Count ?? 0,
                state?.ActiveSkill);
            return state;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveAsync(ConversationState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateSessionId(state.SessionId);

        Directory.CreateDirectory(_rootDirectory);

        var filePath = GetFilePath(state.SessionId);
        var tempFilePath = filePath + ".tmp";

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            }

            File.Move(tempFilePath, filePath, overwrite: true);
            _logger?.LogDebug(
                "FileSessionStore SaveAsync. SessionId={SessionId}, HistoryCount={HistoryCount}, ActiveSkill={ActiveSkill}",
                state.SessionId,
                state.History?.Count ?? 0,
                state.ActiveSkill);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            _mutex.Release();
        }
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);

        var filePath = GetFilePath(sessionId);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            _logger?.LogDebug("FileSessionStore DeleteAsync. SessionId={SessionId}", sessionId);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private string GetFilePath(string sessionId)
    {
        var fileName = ToSafeFileName(sessionId) + ".json";
        return Path.Combine(_rootDirectory, fileName);
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId is required", nameof(sessionId));
        }
    }

    private static string ToSafeFileName(string sessionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
