namespace LlmMacos.Core.Models;

public sealed record ModelFile(
    string Name,
    long? Size,
    string? Sha,
    string? Rfilename)
{
    public bool IsGguf => Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);
}
