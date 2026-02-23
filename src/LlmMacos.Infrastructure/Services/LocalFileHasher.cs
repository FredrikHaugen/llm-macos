using System.Security.Cryptography;
using LlmMacos.Core.Abstractions;

namespace LlmMacos.Infrastructure.Services;

public sealed class LocalFileHasher : ILocalFileHasher
{
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var file = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(file, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
