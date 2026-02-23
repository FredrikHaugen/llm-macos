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
                    Label = progress.DownloadId,
                    Status = progress.Status,
                    Message = progress.Message ?? string.Empty
                };

                Items.Insert(0, existing);
            }

            existing.Status = progress.Status;
            existing.BytesDownloaded = progress.BytesDownloaded;
            existing.TotalBytes = progress.TotalBytes ?? 0;
            existing.Percent = progress.Percent ?? 0;
            existing.Message = progress.Message ?? progress.Status.ToString();

            StatusMessage = progress.Status switch
            {
                DownloadStatus.Downloading => "Downloads in progress...",
                DownloadStatus.Completed => "Latest download completed.",
                DownloadStatus.Failed => "A download failed.",
                DownloadStatus.Cancelled => "Download cancelled.",
                _ => "No active downloads."
            };
        });
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
