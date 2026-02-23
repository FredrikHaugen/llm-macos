using LlmMacos.Core.Models;

namespace LlmMacos.Core.Abstractions;

public interface IChatSessionService
{
    Task<ChatSession> CreateAsync(string modelId, CancellationToken ct);

    Task SaveAsync(ChatSession session, CancellationToken ct);

    Task<IReadOnlyList<ChatSessionSummary>> ListAsync(CancellationToken ct);

    Task<ChatSession?> GetAsync(string sessionId, CancellationToken ct);
}
