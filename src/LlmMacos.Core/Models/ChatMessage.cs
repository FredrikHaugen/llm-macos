namespace LlmMacos.Core.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
