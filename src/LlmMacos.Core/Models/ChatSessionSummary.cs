namespace LlmMacos.Core.Models;

public sealed record ChatSessionSummary(
    string SessionId,
    string ModelId,
    string Title,
    DateTimeOffset UpdatedAt,
    int MessageCount);
