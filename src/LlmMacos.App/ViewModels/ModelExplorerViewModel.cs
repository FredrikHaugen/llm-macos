using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmMacos.App.Models;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using Microsoft.Extensions.Logging;

namespace LlmMacos.App.ViewModels;

public sealed partial class ModelExplorerViewModel : ViewModelBase
{
    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IDownloadService _downloadService;
    private readonly IModelRegistryService _registryService;
    private readonly ISecretStore _secretStore;
    private readonly IAppPathsProvider _pathsProvider;
    private readonly ILocalFileHasher _hasher;
    private readonly ILogger<ModelExplorerViewModel> _logger;

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
    }

    public event EventHandler<LocalModel>? ModelDownloaded;

    public ObservableCollection<ModelSearchItem> Results { get; }

    public ObservableCollection<ModelFile> SelectedFiles { get; }

    [ObservableProperty]
    private string searchText = "llama";

    [ObservableProperty]
    private ModelSearchItem? selectedResult;

    [ObservableProperty]
    private ModelFile? selectedFile;

    [ObservableProperty]
    private string statusMessage = "Search Hugging Face for GGUF models.";

    [ObservableProperty]
    private bool isBusy;

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
            var models = await _huggingFaceClient.SearchModelsAsync(new ModelSearchQuery(SearchText, Limit: 40), CancellationToken.None);
            Results.Clear();
            foreach (var model in models.Select(ModelSearchItem.FromResult))
            {
                Results.Add(model);
            }

            SelectedResult = Results.FirstOrDefault();
            SelectedFile = SelectedResult?.GgufFiles.FirstOrDefault();
            StatusMessage = Results.Count == 0 ? "No GGUF models found." : $"Found {Results.Count} models.";
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

        IsBusy = true;
        StatusMessage = $"Downloading {SelectedFile.Name}...";

        try
        {
            var downloadId = Guid.NewGuid().ToString("N");
            var safeModelFile = SelectedResult.RepoId.Replace("/", "_", StringComparison.Ordinal) + "__" + Path.GetFileName(SelectedFile.Name);
            var destinationPath = Path.Combine(_pathsProvider.Paths.Models, safeModelFile);
            var token = await _secretStore.GetHfTokenAsync(CancellationToken.None);
            var request = new DownloadRequest(
                DownloadId: downloadId,
                RepoId: SelectedResult.RepoId,
                Revision: "main",
                FileName: SelectedFile.Name,
                DestinationPath: destinationPath,
                BearerToken: token);

            var handle = await _downloadService.QueueAsync(request, CancellationToken.None);
            await handle.Completion;

            var fileInfo = new FileInfo(destinationPath);
            var sha = await _hasher.ComputeSha256Async(destinationPath, CancellationToken.None);
            var stableModelId = $"{SelectedResult.RepoId}:{SelectedFile.Name}";

            var localModel = new LocalModel(
                ModelId: stableModelId,
                RepoId: SelectedResult.RepoId,
                Revision: "main",
                FileName: SelectedFile.Name,
                LocalPath: destinationPath,
                Bytes: fileInfo.Length,
                DownloadedAt: DateTimeOffset.UtcNow,
                Sha256Optional: sha,
                LastUsedAt: null);

            await _registryService.UpsertAsync(localModel, CancellationToken.None);
            ModelDownloaded?.Invoke(this, localModel);

            StatusMessage = "Download complete and model registered.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download selected model");
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
