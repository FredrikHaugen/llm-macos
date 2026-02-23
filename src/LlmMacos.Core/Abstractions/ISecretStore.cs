namespace LlmMacos.Core.Abstractions;

public interface ISecretStore
{
    Task SetHfTokenAsync(string token, CancellationToken ct);

    Task<string?> GetHfTokenAsync(CancellationToken ct);

    Task ClearHfTokenAsync(CancellationToken ct);
}
