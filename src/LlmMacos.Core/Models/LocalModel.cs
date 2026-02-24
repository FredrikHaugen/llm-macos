namespace LlmMacos.Core.Models;

public sealed record LocalModel(
    string ModelId,
    string RepoId,
    string Revision,
    string FileName,
    string? PipelineTag,
    bool IsLlm,
    string LocalPath,
    long Bytes,
    DateTimeOffset DownloadedAt,
    string? Sha256Optional,
    DateTimeOffset? LastUsedAt)
{
    public ModelState State { get; init; } = ModelState.Downloaded;
}
