namespace LlmMacos.Core.Models;

public sealed record DownloadProgress(
    string DownloadId,
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
