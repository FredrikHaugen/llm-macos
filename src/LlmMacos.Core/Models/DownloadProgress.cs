namespace LlmMacos.Core.Models;

public sealed record DownloadProgress(
    string DownloadId,
    string? RepoId,
    string? FileName,
    DownloadStatus Status,
    long BytesDownloaded,
    long? TotalBytes,
    double? Percent,
    double? BytesPerSecond,
    TimeSpan? Eta,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;
}
