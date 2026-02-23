namespace LlmMacos.Core.Models;

public sealed record InferenceConfig(
    uint ContextSize = 4096,
    int GpuLayerCount = 99,
    float Temperature = 0.7f,
    float TopP = 0.9f,
    int MaxTokens = 512,
    bool UseMetal = true,
    string? SystemPrompt = null);
