using FluentAssertions;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using LlmMacos.Infrastructure.Services;

namespace LlmMacos.Infrastructure.Tests;

public sealed class ModelRegistryServiceTests
{
    [Fact]
    public async Task ReconcileAsync_MarksMissingFilesAsError()
    {
        using var fixture = new TempPathsFixture();
        IAppPathsProvider paths = fixture;
        var sut = new ModelRegistryService(paths);

        var missingModel = new LocalModel(
            ModelId: "id-1",
            RepoId: "org/model",
            Revision: "main",
            FileName: "model.gguf",
            LocalPath: Path.Combine(paths.Paths.Models, "missing.gguf"),
            Bytes: 100,
            DownloadedAt: DateTimeOffset.UtcNow,
            Sha256Optional: null,
            LastUsedAt: null);

        await sut.UpsertAsync(missingModel, CancellationToken.None);
        await sut.ReconcileAsync(CancellationToken.None);

        var models = await sut.ListAsync(CancellationToken.None);
        models.Should().ContainSingle();
        models[0].State.Should().Be(ModelState.Error);
    }

    private sealed class TempPathsFixture : IAppPathsProvider, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "llm-macos-tests", Guid.NewGuid().ToString("N"));

        public AppPaths Paths => new(
            Root: _root,
            Models: Path.Combine(_root, "models"),
            Downloads: Path.Combine(_root, "downloads"),
            Chats: Path.Combine(_root, "chats"),
            Settings: Path.Combine(_root, "settings"),
            Logs: Path.Combine(_root, "logs"),
            ModelRegistryFile: Path.Combine(_root, "model-registry.json"),
            SettingsFile: Path.Combine(_root, "settings", "settings.json"));

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Paths.Root);
            Directory.CreateDirectory(Paths.Models);
            Directory.CreateDirectory(Paths.Downloads);
            Directory.CreateDirectory(Paths.Chats);
            Directory.CreateDirectory(Paths.Settings);
            Directory.CreateDirectory(Paths.Logs);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
    }
}
