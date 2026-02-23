namespace LlmMacos.Core.Models;

public sealed record AppSettings(
    string? DefaultSystemPrompt,
    InferenceConfig DefaultInferenceConfig,
    string? LastModelId,
    DateTimeOffset UpdatedAt)
{
    public static AppSettings Default => new(
        DefaultSystemPrompt: "You are a helpful assistant running locally on macOS.",
        DefaultInferenceConfig: new InferenceConfig(),
        LastModelId: null,
        UpdatedAt: DateTimeOffset.UtcNow);
}
