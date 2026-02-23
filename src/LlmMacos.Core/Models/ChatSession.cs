namespace LlmMacos.Core.Models;

public sealed record ChatSession(
    string SessionId,
    string ModelId,
    string Title,
    List<ChatMessage> Messages,
    InferenceConfig Config,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
