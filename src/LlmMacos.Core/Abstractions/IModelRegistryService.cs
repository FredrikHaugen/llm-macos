using LlmMacos.Core.Models;

namespace LlmMacos.Core.Abstractions;

public interface IModelRegistryService
{
    Task<IReadOnlyList<LocalModel>> ListAsync(CancellationToken ct);

    Task UpsertAsync(LocalModel model, CancellationToken ct);

    Task RemoveAsync(string modelId, CancellationToken ct);

    Task ReconcileAsync(CancellationToken ct);
}
