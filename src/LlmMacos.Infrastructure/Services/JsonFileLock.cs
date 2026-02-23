namespace LlmMacos.Infrastructure.Services;

internal sealed class JsonFileLock
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public async Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);

        try
        {
            return await action();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task WithLockAsync(Func<Task> action, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);

        try
        {
            await action();
        }
        finally
        {
            _mutex.Release();
        }
    }
}
