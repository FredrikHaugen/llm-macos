namespace LlmMacos.Core.Models;

public sealed record DownloadRequest(
    string DownloadId,
    string RepoId,
    string Revision,
    string FileName,
    string DestinationPath,
    string? BearerToken = null);
