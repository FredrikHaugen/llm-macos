namespace LlmMacos.Core.Models;

public sealed record TokenChunk(
    string Text,
    bool IsCompleted = false,
    string? FinishReason = null);
