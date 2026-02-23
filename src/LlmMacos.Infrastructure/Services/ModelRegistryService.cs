using System.Text.Json;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.Infrastructure.Services;

public sealed class ModelRegistryService : IModelRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IAppPathsProvider _pathsProvider;
    private readonly JsonFileLock _fileLock = new();

    public ModelRegistryService(IAppPathsProvider pathsProvider)
    {
        _pathsProvider = pathsProvider;
    }

    public async Task<IReadOnlyList<LocalModel>> ListAsync(CancellationToken ct)
    {
        return await _fileLock.WithLockAsync(async () =>
        {
            var models = await LoadCoreAsync(ct);
            return models
                .Select(ApplyRuntimeState)
                .OrderByDescending(m => m.LastUsedAt ?? m.DownloadedAt)
                .ToList();
        }, ct);
    }

    public async Task UpsertAsync(LocalModel model, CancellationToken ct)
    {
        await _fileLock.WithLockAsync(async () =>
        {
            var models = await LoadCoreAsync(ct);
            var idx = models.FindIndex(m => m.ModelId.Equals(model.ModelId, StringComparison.Ordinal));
            var normalized = ApplyRuntimeState(model);

            if (idx >= 0)
            {
                models[idx] = normalized;
            }
            else
            {
                models.Add(normalized);
            }

            await SaveCoreAsync(models, ct);
        }, ct);
    }

    public async Task RemoveAsync(string modelId, CancellationToken ct)
    {
        await _fileLock.WithLockAsync(async () =>
        {
            var models = await LoadCoreAsync(ct);
            models.RemoveAll(m => m.ModelId.Equals(modelId, StringComparison.Ordinal));
            await SaveCoreAsync(models, ct);
        }, ct);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        await _fileLock.WithLockAsync(async () =>
        {
            var models = await LoadCoreAsync(ct);
            var reconciled = models.Select(ApplyRuntimeState).ToList();
            await SaveCoreAsync(reconciled, ct);
        }, ct);
    }

    private async Task<List<LocalModel>> LoadCoreAsync(CancellationToken ct)
    {
        _pathsProvider.EnsureCreated();
        var path = _pathsProvider.Paths.ModelRegistryFile;

        if (!File.Exists(path))
        {
            await SaveCoreAsync([], ct);
            return [];
        }

        await using var file = File.OpenRead(path);
        var models = await JsonSerializer.DeserializeAsync<List<LocalModel>>(file, JsonOptions, ct);
        return models ?? [];
    }

    private async Task SaveCoreAsync(List<LocalModel> models, CancellationToken ct)
    {
        _pathsProvider.EnsureCreated();

        var tempPath = _pathsProvider.Paths.ModelRegistryFile + ".tmp";
        await using (var file = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(file, models, JsonOptions, ct);
        }

        File.Move(tempPath, _pathsProvider.Paths.ModelRegistryFile, true);
    }

    private static LocalModel ApplyRuntimeState(LocalModel model)
    {
        if (!File.Exists(model.LocalPath))
        {
            return model with { State = ModelState.Error };
        }

        var info = new FileInfo(model.LocalPath);
        if (!model.LocalPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || info.Length <= 0)
        {
            return model with { State = ModelState.Error };
        }

        return model with { State = ModelState.Downloaded };
    }
}
