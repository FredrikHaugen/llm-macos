using System.Text.Json;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.Infrastructure.Services;

public sealed class ChatSessionService : IChatSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IAppPathsProvider _pathsProvider;
    private readonly JsonFileLock _fileLock = new();

    public ChatSessionService(IAppPathsProvider pathsProvider)
    {
        _pathsProvider = pathsProvider;
    }

    public Task<ChatSession> CreateAsync(string modelId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid().ToString("N");

        var session = new ChatSession(
            SessionId: sessionId,
            ModelId: modelId,
            Title: "New Chat",
            Messages: [],
            Config: new InferenceConfig(),
            CreatedAt: now,
            UpdatedAt: now);

        return Task.FromResult(session);
    }

    public async Task SaveAsync(ChatSession session, CancellationToken ct)
    {
        await _fileLock.WithLockAsync(async () =>
        {
            _pathsProvider.EnsureCreated();

            var updated = session with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Title = InferTitle(session)
            };

            var tempPath = Path.Combine(_pathsProvider.Paths.Chats, $"{session.SessionId}.json.tmp");
            var finalPath = Path.Combine(_pathsProvider.Paths.Chats, $"{session.SessionId}.json");

            await using (var file = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(file, updated, JsonOptions, ct);
            }

            File.Move(tempPath, finalPath, true);
        }, ct);
    }

    public async Task<IReadOnlyList<ChatSessionSummary>> ListAsync(CancellationToken ct)
    {
        return await _fileLock.WithLockAsync(async () =>
        {
            _pathsProvider.EnsureCreated();

            var summaries = new List<ChatSessionSummary>();
            foreach (var file in Directory.EnumerateFiles(_pathsProvider.Paths.Chats, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(file);
                var session = await JsonSerializer.DeserializeAsync<ChatSession>(stream, JsonOptions, ct);

                if (session is null)
                {
                    continue;
                }

                summaries.Add(new ChatSessionSummary(
                    SessionId: session.SessionId,
                    ModelId: session.ModelId,
                    Title: InferTitle(session),
                    UpdatedAt: session.UpdatedAt,
                    MessageCount: session.Messages.Count));
            }

            return summaries.OrderByDescending(s => s.UpdatedAt).ToList();
        }, ct);
    }

    public async Task<ChatSession?> GetAsync(string sessionId, CancellationToken ct)
    {
        return await _fileLock.WithLockAsync(async () =>
        {
            _pathsProvider.EnsureCreated();

            var path = Path.Combine(_pathsProvider.Paths.Chats, $"{sessionId}.json");
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ChatSession>(stream, JsonOptions, ct);
        }, ct);
    }

    private static string InferTitle(ChatSession session)
    {
        var userMessage = session.Messages
            .FirstOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));

        if (userMessage is null)
        {
            return session.Title;
        }

        var normalized = userMessage.Content.Trim();
        if (normalized.Length <= 48)
        {
            return normalized;
        }

        return normalized[..45] + "...";
    }
}
