using System.Text.Json;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IAppPathsProvider _pathsProvider;
    private readonly JsonFileLock _fileLock = new();

    public SettingsService(IAppPathsProvider pathsProvider)
    {
        _pathsProvider = pathsProvider;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        return await _fileLock.WithLockAsync(async () =>
        {
            _pathsProvider.EnsureCreated();
            var path = _pathsProvider.Paths.SettingsFile;

            if (!File.Exists(path))
            {
                var settings = AppSettings.Default;
                await SaveCoreAsync(settings, ct);
                return settings;
            }

            await using var file = File.OpenRead(path);
            var settingsFromDisk = await JsonSerializer.DeserializeAsync<AppSettings>(file, JsonOptions, ct);
            return settingsFromDisk ?? AppSettings.Default;
        }, ct);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        await _fileLock.WithLockAsync(async () =>
        {
            await SaveCoreAsync(settings with
            {
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);
        }, ct);
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken ct)
    {
        _pathsProvider.EnsureCreated();

        var tempPath = _pathsProvider.Paths.SettingsFile + ".tmp";
        await using (var file = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(file, settings, JsonOptions, ct);
        }

        File.Move(tempPath, _pathsProvider.Paths.SettingsFile, true);
    }
}
