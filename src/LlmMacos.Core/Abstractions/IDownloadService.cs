using LlmMacos.Core.Models;

namespace LlmMacos.Core.Abstractions;

public interface IDownloadService
{
    Task<DownloadHandle> QueueAsync(DownloadRequest request, CancellationToken ct);

    IObservable<DownloadProgress> ProgressStream { get; }

    Task CancelAsync(string downloadId);
}
