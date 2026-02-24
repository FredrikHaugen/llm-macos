using FluentAssertions;
using LlmMacos.App.ViewModels;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LlmMacos.App.Tests;

public sealed class ChatViewModelTests
{
    [Fact]
    public async Task RefreshModelsAsync_ShowsOnlyLlmModels()
    {
        var modelRegistry = new Mock<IModelRegistryService>();
        modelRegistry
            .Setup(x => x.ReconcileAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var now = DateTimeOffset.UtcNow;
        modelRegistry
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LocalModel(
                    ModelId: "llm",
                    RepoId: "org/llm",
                    Revision: "main",
                    FileName: "model.gguf",
                    PipelineTag: "text-generation",
                    IsLlm: true,
                    LocalPath: "/tmp/model.gguf",
                    Bytes: 100,
                    DownloadedAt: now,
                    Sha256Optional: null,
                    LastUsedAt: null),
                new LocalModel(
                    ModelId: "vision",
                    RepoId: "org/vision",
                    Revision: "main",
                    FileName: "vision.gguf",
                    PipelineTag: "image-classification",
                    IsLlm: false,
                    LocalPath: "/tmp/vision.gguf",
                    Bytes: 100,
                    DownloadedAt: now,
                    Sha256Optional: null,
                    LastUsedAt: null)
            ]);

        var inference = new Mock<IInferenceService>();
        var sessions = new Mock<IChatSessionService>();
        var settings = new Mock<ISettingsService>();

        var sut = new ChatViewModel(
            modelRegistry.Object,
            inference.Object,
            sessions.Object,
            settings.Object,
            NullLogger<ChatViewModel>.Instance);

        await sut.RefreshModelsAsync();

        sut.AvailableModels.Should().HaveCount(1);
        sut.AvailableModels.Single().RepoId.Should().Be("org/llm");
    }
}
