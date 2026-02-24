using LlmMacos.Core.Models;

namespace LlmMacos.App.Models;

public sealed record ModelSearchItem(
    string RepoId,
    string? Author,
    string? Description,
    string? PipelineTag,
    bool IsLlm,
    long Downloads,
    int Likes,
    DateTimeOffset? LastModified,
    IReadOnlyList<ModelFile> GgufFiles)
{
    public static ModelSearchItem FromResult(ModelSearchResult result)
    {
        var gguf = result.Files.Where(f => f.IsGguf).ToList();
        return new ModelSearchItem(
            RepoId: result.RepoId,
            Author: result.Author,
            Description: result.Description,
            PipelineTag: result.PipelineTag,
            IsLlm: result.IsLlm,
            Downloads: result.Downloads,
            Likes: result.Likes,
            LastModified: result.LastModified,
            GgufFiles: gguf);
    }

    public override string ToString() => RepoId;
}
