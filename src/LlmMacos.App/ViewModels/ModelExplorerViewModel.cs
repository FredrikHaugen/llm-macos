using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmMacos.App.Models;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using Microsoft.Extensions.Logging;

namespace LlmMacos.App.ViewModels;

public sealed partial class ModelExplorerViewModel : ViewModelBase, IDisposable
{
    private const int MaxRecentDownloads = 25;

    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IDownloadService _downloadService;
    private readonly IModelRegistryService _registryService;
    private readonly ISecretStore _secretStore;
    private readonly IAppPathsProvider _pathsProvider;
    private readonly ILocalFileHasher _hasher;
    private readonly ILogger<ModelExplorerViewModel> _logger;
    private readonly IDisposable _progressSubscription;

    public ModelExplorerViewModel(
        IHuggingFaceClient huggingFaceClient,
        IDownloadService downloadService,
        IModelRegistryService registryService,
        ISecretStore secretStore,
        IAppPathsProvider pathsProvider,
        ILocalFileHasher hasher,
        ILogger<ModelExplorerViewModel> logger)
    {
        _huggingFaceClient = huggingFaceClient;
        _downloadService = downloadService;
        _registryService = registryService;
        _secretStore = secretStore;
        _pathsProvider = pathsProvider;
        _hasher = hasher;
        _logger = logger;

        Results = [];
        SelectedFiles = [];
        RecentDownloads = [];

        _progressSubscription = _downloadService.ProgressStream.Subscribe(new ProgressObserver(OnProgress));
    }

    public event EventHandler<LocalModel>? ModelDownloaded;

    public ObservableCollection<ModelSearchItem> Results { get; }

    public ObservableCollection<ModelFile> SelectedFiles { get; }

    public ObservableCollection<DownloadItem> RecentDownloads { get; }

    [ObservableProperty]
    private string searchText = "llama";

    [ObservableProperty]
    private ModelSearchItem? selectedResult;

    [ObservableProperty]
    private ModelFile? selectedFile;

    [ObservableProperty]
    private string statusMessage = "Search Hugging Face for GGUF LLM models.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? activeDownloadId;

    [ObservableProperty]
    private string activeDownloadLabel = string.Empty;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private double? downloadPercent;

    [ObservableProperty]
    private long downloadBytesDownloaded;

    [ObservableProperty]
    private long? downloadTotalBytes;

    [ObservableProperty]
    private double? downloadSpeedBytesPerSecond;

    [ObservableProperty]
    private TimeSpan? downloadEta;

    [ObservableProperty]
    private bool isDownloadIndeterminate;

    [ObservableProperty]
    private string downloadProgressText = string.Empty;

    [ObservableProperty]
    private string downloadSpeedText = string.Empty;

    [ObservableProperty]
    private string downloadEtaText = string.Empty;

    [ObservableProperty]
    private DownloadItem? selectedDownload;

    public Task InitializeAsync() => SearchAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Searching Hugging Face...";

        try
        {
            var models = await _huggingFaceClient.SearchModelsAsync(
                new ModelSearchQuery(SearchText, Limit: 40, PipelineTag: "text-generation"),
                CancellationToken.None);

            Results.Clear();
            foreach (var model in models.Select(ModelSearchItem.FromResult))
            {
                Results.Add(model);
            }

            SelectedResult = Results.FirstOrDefault();
            SelectedFile = SelectedResult?.GgufFiles.FirstOrDefault();
            StatusMessage = Results.Count == 0
                ? "No text-generation GGUF models found."
                : $"Found {Results.Count} LLM models.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search models");
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedResultChanged(ModelSearchItem? value)
    {
        SelectedFiles.Clear();
        if (value is null)
        {
            SelectedFile = null;
            return;
        }

        foreach (var file in value.GgufFiles)
        {
            SelectedFiles.Add(file);
        }

        SelectedFile = SelectedFiles.FirstOrDefault();
        _ = LoadSelectedDetailsAsync(value.RepoId);
    }

    private async Task LoadSelectedDetailsAsync(string repoId)
    {
        try
        {
            var details = await _huggingFaceClient.GetModelDetailsAsync(repoId, CancellationToken.None);
            if (SelectedResult is null || !SelectedResult.RepoId.Equals(repoId, StringComparison.Ordinal))
            {
                return;
            }

            var ggufFiles = details.Files.Where(f => f.IsGguf).ToList();
            if (ggufFiles.Count == 0)
            {
                return;
            }

            SelectedFiles.Clear();
            foreach (var file in ggufFiles)
            {
                SelectedFiles.Add(file);
            }

            SelectedFile = SelectedFiles.FirstOrDefault();
            StatusMessage = $"Loaded details for {repoId}.";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load model details for {RepoId}", repoId);
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (IsBusy || SelectedResult is null || SelectedFile is null)
        {
            return;
        }

        if (IsDownloading)
        {
            StatusMessage = "A download is already active. Cancel it or wait for completion.";
            return;
        }

        IsBusy = true;

        try
        {
            var downloadId = Guid.NewGuid().ToString("N");
            var safeModelFile = SelectedResult.RepoId.Replace("/", "_", StringComparison.Ordinal) + "__" + Path.GetFileName(SelectedFile.Name);
            var destinationPath = Path.Combine(_pathsProvider.Paths.Models, safeModelFile);
            var token = await _secretStore.GetHfTokenAsync(CancellationToken.None);

            var context = new PendingDownloadContext(
                DownloadId: downloadId,
                SearchItem: SelectedResult,
                File: SelectedFile,
                DestinationPath: destinationPath,
                StableModelId: $"{SelectedResult.RepoId}:{SelectedFile.Name}");

            ResetDownloadUi(context);

            var request = new DownloadRequest(
                DownloadId: context.DownloadId,
                RepoId: context.SearchItem.RepoId,
                Revision: "main",
                FileName: context.File.Name,
                DestinationPath: context.DestinationPath,
                BearerToken: token);

            var handle = await _downloadService.QueueAsync(request, CancellationToken.None);
            StatusMessage = $"Started download: {ActiveDownloadLabel}";
            _ = TrackDownloadCompletionAsync(handle, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue selected model download");
            StatusMessage = $"Download queue failed: {ex.Message}";
            IsDownloading = false;
            ActiveDownloadId = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelActiveDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveDownloadId))
        {
            return;
        }

        await _downloadService.CancelAsync(ActiveDownloadId);
    }

    [RelayCommand]
    private async Task CancelSelectedDownloadAsync()
    {
        if (SelectedDownload is null)
        {
            return;
        }

        await _downloadService.CancelAsync(SelectedDownload.DownloadId);
    }

    [RelayCommand]
    private async Task CancelDownloadAsync(string? downloadId)
    {
        if (string.IsNullOrWhiteSpace(downloadId))
        {
            return;
        }

        await _downloadService.CancelAsync(downloadId);
    }

    private async Task TrackDownloadCompletionAsync(DownloadHandle handle, PendingDownloadContext context)
    {
        try
        {
            await handle.Completion;

            var fileInfo = new FileInfo(context.DestinationPath);
            var sha = await _hasher.ComputeSha256Async(context.DestinationPath, CancellationToken.None);

            var localModel = new LocalModel(
                ModelId: context.StableModelId,
                RepoId: context.SearchItem.RepoId,
                Revision: "main",
                FileName: context.File.Name,
                PipelineTag: context.SearchItem.PipelineTag,
                IsLlm: context.SearchItem.IsLlm,
                LocalPath: context.DestinationPath,
                Bytes: fileInfo.Length,
                DownloadedAt: DateTimeOffset.UtcNow,
                Sha256Optional: sha,
                LastUsedAt: null);

            await _registryService.UpsertAsync(localModel, CancellationToken.None);
            ModelDownloaded?.Invoke(this, localModel);

            StatusMessage = "Download complete and model registered.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize selected model download");
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            if (ActiveDownloadId == context.DownloadId)
            {
                IsDownloading = false;
                ActiveDownloadId = null;
            }
        }
    }

    private void OnProgress(DownloadProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = UpsertRecentDownload(progress);
            if (!string.IsNullOrWhiteSpace(ActiveDownloadId) && progress.DownloadId.Equals(ActiveDownloadId, StringComparison.Ordinal))
            {
                UpdateActiveDownloadUi(progress);

                if (!string.IsNullOrWhiteSpace(progress.Message))
                {
                    StatusMessage = progress.Message;
                }
                else
                {
                    StatusMessage = progress.Status switch
                    {
                        DownloadStatus.Downloading => $"Downloading {item.Label}",
                        DownloadStatus.Completed => $"Completed {item.Label}",
                        DownloadStatus.Failed => $"Failed {item.Label}",
                        DownloadStatus.Cancelled => $"Cancelled {item.Label}",
                        _ => StatusMessage
                    };
                }

                if (progress.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled)
                {
                    IsDownloading = false;
                    ActiveDownloadId = null;
                }
            }
        });
    }

    private void UpdateActiveDownloadUi(DownloadProgress progress)
    {
        if (progress.BytesDownloaded > 0 || DownloadBytesDownloaded == 0 || progress.Status == DownloadStatus.Queued)
        {
            DownloadBytesDownloaded = progress.BytesDownloaded;
        }

        if (progress.TotalBytes.HasValue)
        {
            DownloadTotalBytes = progress.TotalBytes;
        }

        if (progress.Percent.HasValue)
        {
            DownloadPercent = progress.Percent;
        }

        if (progress.Status == DownloadStatus.Completed)
        {
            DownloadPercent = 100d;
            IsDownloadIndeterminate = false;
        }
        else
        {
            IsDownloadIndeterminate = progress.Status == DownloadStatus.Downloading && !progress.TotalBytes.HasValue;
        }

        DownloadSpeedBytesPerSecond = progress.BytesPerSecond;
        DownloadEta = progress.Eta;
        DownloadProgressText = BuildProgressText(DownloadBytesDownloaded, DownloadTotalBytes);
        DownloadSpeedText = progress.BytesPerSecond is > 0
            ? $"{DownloadItem.FormatBytes(progress.BytesPerSecond.Value)}/s"
            : string.Empty;
        DownloadEtaText = progress.Eta.HasValue && progress.Eta.Value > TimeSpan.Zero
            ? $"ETA {progress.Eta.Value:mm\\:ss}"
            : string.Empty;
    }

    private DownloadItem UpsertRecentDownload(DownloadProgress progress)
    {
        var existing = RecentDownloads.FirstOrDefault(i => i.DownloadId.Equals(progress.DownloadId, StringComparison.Ordinal));
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

            RecentDownloads.Insert(0, existing);
            if (RecentDownloads.Count > MaxRecentDownloads)
            {
                RecentDownloads.RemoveAt(RecentDownloads.Count - 1);
            }
        }
        else if (!string.IsNullOrWhiteSpace(progress.RepoId) && !string.IsNullOrWhiteSpace(progress.FileName))
        {
            existing.Label = BuildLabel(progress);
        }

        existing.Status = progress.Status;
        if (progress.BytesDownloaded > 0 || existing.BytesDownloaded == 0 || progress.Status == DownloadStatus.Queued)
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
        existing.ProgressText = BuildItemProgressText(existing);
        existing.SpeedText = progress.BytesPerSecond is > 0
            ? $"{DownloadItem.FormatBytes(progress.BytesPerSecond.Value)}/s"
            : string.Empty;
        existing.EtaText = progress.Eta.HasValue && progress.Eta.Value > TimeSpan.Zero
            ? $"ETA {progress.Eta.Value:mm\\:ss}"
            : string.Empty;

        return existing;
    }

    private static string BuildLabel(DownloadProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.RepoId) && !string.IsNullOrWhiteSpace(progress.FileName))
        {
            return $"{progress.RepoId} / {Path.GetFileName(progress.FileName)}";
        }

        return progress.DownloadId;
    }

    private static string BuildItemProgressText(DownloadItem item)
    {
        var downloaded = DownloadItem.FormatBytes(item.BytesDownloaded);
        if (item.TotalBytes is > 0)
        {
            var total = DownloadItem.FormatBytes(item.TotalBytes.Value);
            return $"{downloaded} / {total}";
        }

        return $"{downloaded} downloaded";
    }

    private void ResetDownloadUi(PendingDownloadContext context)
    {
        ActiveDownloadId = context.DownloadId;
        ActiveDownloadLabel = $"{context.SearchItem.RepoId} / {Path.GetFileName(context.File.Name)}";
        IsDownloading = true;

        DownloadPercent = 0;
        DownloadBytesDownloaded = 0;
        DownloadTotalBytes = null;
        DownloadSpeedBytesPerSecond = null;
        DownloadEta = null;
        IsDownloadIndeterminate = true;
        DownloadProgressText = "0 B downloaded";
        DownloadSpeedText = string.Empty;
        DownloadEtaText = string.Empty;
    }

    private static string BuildProgressText(long downloadedBytes, long? totalBytes)
    {
        var downloaded = DownloadItem.FormatBytes(downloadedBytes);
        if (totalBytes is > 0)
        {
            var total = DownloadItem.FormatBytes(totalBytes.Value);
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

    private sealed record PendingDownloadContext(
        string DownloadId,
        ModelSearchItem SearchItem,
        ModelFile File,
        string DestinationPath,
        string StableModelId);
}
