namespace LlmMacos.Infrastructure.Options;

public sealed class HuggingFaceOptions
{
    public const string SectionName = "HuggingFace";

    public string ApiBaseUrl { get; init; } = "https://huggingface.co";
}
