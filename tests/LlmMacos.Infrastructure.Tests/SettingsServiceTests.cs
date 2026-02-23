using FluentAssertions;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using LlmMacos.Infrastructure.Services;

namespace LlmMacos.Infrastructure.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var fixture = new TempPathsFixture();
        IAppPathsProvider paths = fixture;
        var sut = new SettingsService(paths);

        var settings = new AppSettings(
            DefaultSystemPrompt: "hello",
            DefaultInferenceConfig: new InferenceConfig(ContextSize: 2048, Temperature: 0.3f, TopP: 0.95f, MaxTokens: 128),
            LastModelId: "model-1",
            UpdatedAt: DateTimeOffset.UtcNow);

        await sut.SaveAsync(settings, CancellationToken.None);
        var loaded = await sut.LoadAsync(CancellationToken.None);

        loaded.DefaultSystemPrompt.Should().Be("hello");
        loaded.DefaultInferenceConfig.ContextSize.Should().Be(2048);
        loaded.DefaultInferenceConfig.MaxTokens.Should().Be(128);
        loaded.LastModelId.Should().Be("model-1");
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
