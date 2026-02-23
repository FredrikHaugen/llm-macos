namespace LlmMacos.Core.Models;

public sealed record ChatTurn(
    string UserMessage,
    IReadOnlyList<ChatMessage> History,
    InferenceConfig Config);
