using LlmMacos.Core.Models;

namespace LlmMacos.Core.Abstractions;

public interface IHuggingFaceClient
{
    Task<IReadOnlyList<ModelSearchResult>> SearchModelsAsync(ModelSearchQuery query, CancellationToken ct);

    Task<ModelDetails> GetModelDetailsAsync(string repoId, CancellationToken ct);

    Uri BuildDownloadUri(string repoId, string revision, string fileName);
}
