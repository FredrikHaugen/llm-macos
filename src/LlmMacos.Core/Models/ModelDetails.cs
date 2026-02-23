namespace LlmMacos.Core.Models;

public sealed record ModelDetails(
    string RepoId,
    string? Sha,
    string? LastModified,
    bool IsPrivate,
    IReadOnlyList<ModelFile> Files,
    IReadOnlyDictionary<string, string>? CardData);
