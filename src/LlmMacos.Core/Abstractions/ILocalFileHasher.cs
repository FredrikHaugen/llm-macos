namespace LlmMacos.Core.Abstractions;

public interface ILocalFileHasher
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct);
}
