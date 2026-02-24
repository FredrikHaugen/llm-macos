namespace LlmMacos.Core.Models;

public sealed record ModelSearchResult(
    string RepoId,
    string? Author,
    string? Description,
    string? PipelineTag,
    IReadOnlyList<string> Tags,
    bool IsLlm,
    long Downloads,
    int Likes,
    DateTimeOffset? LastModified,
    bool IsPrivate,
    IReadOnlyList<ModelFile> Files);
