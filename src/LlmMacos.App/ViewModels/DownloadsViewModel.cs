using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmMacos.App.Models;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.App.ViewModels;

public sealed partial class DownloadsViewModel : ViewModelBase, IDisposable
{
    private readonly IDownloadService _downloadService;
    private readonly IDisposable _progressSubscription;

    public DownloadsViewModel(IDownloadService downloadService)
    {
        _downloadService = downloadService;
        Items = [];

        _progressSubscription = _downloadService.ProgressStream.Subscribe(new ProgressObserver(OnProgress));
    }

    public ObservableCollection<DownloadItem> Items { get; }

    [ObservableProperty]
    private DownloadItem? selectedItem;

    [ObservableProperty]
    private string statusMessage = "No active downloads.";

    [RelayCommand]
    private async Task CancelSelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        await _downloadService.CancelAsync(SelectedItem.DownloadId);
    }

    private void OnProgress(DownloadProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Items.FirstOrDefault(i => i.DownloadId.Equals(progress.DownloadId, StringComparison.Ordinal));
            if (existing is null)
            {
                existing = new DownloadItem
                {
                    DownloadId = progress.DownloadId,
                    Label = BuildLabel(progress),
                    Status = progress.Status,
                    Message = progress.Message ?? string.Empty,
                    Percent = 0,
                    ProgressText = "0 B downloaded"
                };

                Items.Insert(0, existing);
            }
            else if (!string.IsNullOrWhiteSpace(progress.RepoId) && !string.IsNullOrWhiteSpace(progress.FileName))
            {
                existing.Label = BuildLabel(progress);
            }

            existing.Status = progress.Status;
            if (progress.BytesDownloaded > 0 || existing.BytesDownloaded == 0 || progress.Status is DownloadStatus.Queued)
            {
                existing.BytesDownloaded = progress.BytesDownloaded;
            }

            if (progress.TotalBytes.HasValue)
            {
                existing.TotalBytes = progress.TotalBytes;
            }

            if (progress.Percent.HasValue)
            {
                existing.Percent = progress.Percent;
            }

            if (progress.Status == DownloadStatus.Completed)
            {
                existing.IsIndeterminate = false;
                existing.Percent = 100d;
            }
            else
            {
                existing.IsIndeterminate = progress.Status == DownloadStatus.Downloading && !progress.TotalBytes.HasValue;
            }

            existing.BytesPerSecond = progress.BytesPerSecond;
            existing.Eta = progress.Eta;
            existing.Message = progress.Message ?? progress.Status.ToString();
            existing.ProgressText = BuildProgressText(existing);
            existing.SpeedText = progress.BytesPerSecond is > 0
                ? $"{DownloadItem.FormatBytes(progress.BytesPerSecond.Value)}/s"
                : string.Empty;
            existing.EtaText = progress.Eta.HasValue && progress.Eta.Value > TimeSpan.Zero
                ? $"ETA {progress.Eta.Value:mm\\:ss}"
                : string.Empty;

            StatusMessage = progress.Status switch
            {
                DownloadStatus.Downloading => $"Downloading {existing.Label}",
                DownloadStatus.Completed => $"Completed {existing.Label}",
                DownloadStatus.Failed => $"Failed {existing.Label}",
                DownloadStatus.Cancelled => $"Cancelled {existing.Label}",
                _ => "No active downloads."
            };
        });
    }

    private static string BuildLabel(DownloadProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.RepoId) && !string.IsNullOrWhiteSpace(progress.FileName))
        {
            return $"{progress.RepoId} / {Path.GetFileName(progress.FileName)}";
        }

        return progress.DownloadId;
    }

    private static string BuildProgressText(DownloadItem item)
    {
        var downloaded = DownloadItem.FormatBytes(item.BytesDownloaded);
        if (item.TotalBytes is > 0)
        {
            var total = DownloadItem.FormatBytes(item.TotalBytes.Value);
            return $"{downloaded} / {total}";
        }

        return $"{downloaded} downloaded";
    }

    public void Dispose()
    {
        _progressSubscription.Dispose();
    }

    private sealed class ProgressObserver(Action<DownloadProgress> onNext) : IObserver<DownloadProgress>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DownloadProgress value)
        {
            onNext(value);
        }
    }
}
