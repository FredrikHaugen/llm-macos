using LlmMacos.Core.Models;

namespace LlmMacos.Core.Abstractions;

public interface IInferenceService
{
    Task LoadModelAsync(LocalModel model, InferenceConfig config, CancellationToken ct);

    IAsyncEnumerable<TokenChunk> StreamReplyAsync(ChatTurn turn, CancellationToken ct);

    Task UnloadAsync(CancellationToken ct);

    LocalModel? ActiveModel { get; }

    bool IsLoaded { get; }
}
