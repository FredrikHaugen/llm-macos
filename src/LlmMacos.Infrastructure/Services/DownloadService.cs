using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reactive.Subjects;
using System.Text.Json;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using LlmMacos.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LlmMacos.Infrastructure.Services;

public sealed class DownloadService : IDownloadService
{
    private static readonly TimeSpan DefaultReadInactivityTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);
    private const int DefaultMaxRetryAttempts = 5;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _readInactivityTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxRetryAttempts;
    private readonly Subject<DownloadProgress> _progressSubject = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new(StringComparer.Ordinal);

    [ActivatorUtilitiesConstructor]
    public DownloadService(HttpClient httpClient)
        : this(httpClient, DefaultReadInactivityTimeout, DefaultMaxRetryAttempts, DefaultRetryDelay)
    {
    }

    public DownloadService(HttpClient httpClient, TimeSpan readInactivityTimeout, int maxRetryAttempts, TimeSpan retryDelay)
    {
        _httpClient = httpClient;
        _readInactivityTimeout = readInactivityTimeout > TimeSpan.Zero
            ? readInactivityTimeout
            : throw new ArgumentOutOfRangeException(nameof(readInactivityTimeout));
        _maxRetryAttempts = maxRetryAttempts >= 0
            ? maxRetryAttempts
            : throw new ArgumentOutOfRangeException(nameof(maxRetryAttempts));
        _retryDelay = retryDelay >= TimeSpan.Zero
            ? retryDelay
            : throw new ArgumentOutOfRangeException(nameof(retryDelay));
    }

    public IObservable<DownloadProgress> ProgressStream => _progressSubject;

    public Task<DownloadHandle> QueueAsync(DownloadRequest request, CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_inFlight.TryAdd(request.DownloadId, linkedCts))
        {
            throw new InvalidOperationException($"Download '{request.DownloadId}' is already running.");
        }

        _progressSubject.OnNext(new DownloadProgress(
            DownloadId: request.DownloadId,
            RepoId: request.RepoId,
            FileName: request.FileName,
            Status: DownloadStatus.Queued,
            BytesDownloaded: 0,
            TotalBytes: null,
            Percent: null,
            BytesPerSecond: null,
            Eta: null,
            Message: "Queued"));

        var completion = Task.Run(() => DownloadCoreAsync(request, linkedCts.Token), linkedCts.Token);
        return Task.FromResult(new DownloadHandle(request.DownloadId, completion));
    }

    public Task CancelAsync(string downloadId)
    {
        if (_inFlight.TryGetValue(downloadId, out var cts))
        {
            cts.Cancel();
        }

        return Task.CompletedTask;
    }

    private async Task DownloadCoreAsync(DownloadRequest request, CancellationToken ct)
    {
        var destinationDir = Path.GetDirectoryName(request.DestinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var partPath = request.DestinationPath + ".part";
        var metaPath = request.DestinationPath + ".part.meta";

        try
        {
            var remote = await GetRemoteMetadataAsync(request, ct);
            var localMeta = await ReadLocalMetadataAsync(metaPath, ct);
            long downloadedBytes = 0;

            if (localMeta is not null && !string.Equals(localMeta.Etag, remote.Etag, StringComparison.Ordinal))
            {
                DeleteFileIfExists(partPath);
                DeleteFileIfExists(metaPath);
                localMeta = null;
            }

            long existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            if (remote.TotalBytes.HasValue && existingBytes > remote.TotalBytes.Value)
            {
                DeleteFileIfExists(partPath);
                existingBytes = 0;
            }

            await SaveLocalMetadataAsync(metaPath, new LocalDownloadMetadata(remote.TotalBytes, remote.Etag), ct);

            var canResume = remote.AcceptRanges;
            var totalBytes = remote.TotalBytes;
            var startingOffset = canResume ? existingBytes : 0;

            if (!canResume && existingBytes > 0)
            {
                DeleteFileIfExists(partPath);
                startingOffset = 0;
            }

            downloadedBytes = startingOffset;
            PublishProgress(
                request,
                DownloadStatus.Downloading,
                downloadedBytes,
                totalBytes,
                ProgressMath.CalculatePercent(downloadedBytes, totalBytes),
                null,
                downloadedBytes > 0 ? "Resuming download..." : "Starting download");

            var attempt = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    downloadedBytes = await DownloadAttemptAsync(
                        request,
                        partPath,
                        canResume,
                        downloadedBytes,
                        totalBytes,
                        ct);
                    break;
                }
                catch (Exception ex) when (IsRetryable(ex, ct) && attempt < _maxRetryAttempts)
                {
                    attempt++;
                    downloadedBytes = GetFileLength(partPath);

                    var message = $"Connection stalled, retrying ({attempt}/{_maxRetryAttempts})...";
                    PublishProgress(
                        request,
                        DownloadStatus.Downloading,
                        downloadedBytes,
                        totalBytes,
                        ProgressMath.CalculatePercent(downloadedBytes, totalBytes),
                        null,
                        message);

                    if (_retryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(_retryDelay, ct);
                    }
                }
            }

            File.Move(partPath, request.DestinationPath, true);
            DeleteFileIfExists(metaPath);

            var finalPercent = ProgressMath.CalculatePercent(downloadedBytes, totalBytes) ?? 100;
            PublishProgress(request, DownloadStatus.Completed, downloadedBytes, totalBytes, finalPercent, null, "Completed", TimeSpan.Zero);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var current = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            var total = await TryReadTotalBytesAsync(metaPath, CancellationToken.None);
            PublishProgress(
                request,
                DownloadStatus.Cancelled,
                current,
                total,
                ProgressMath.CalculatePercent(current, total),
                null,
                "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            var current = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            var total = await TryReadTotalBytesAsync(metaPath, CancellationToken.None);
            PublishProgress(
                request,
                DownloadStatus.Failed,
                current,
                total,
                ProgressMath.CalculatePercent(current, total),
                null,
                ex.Message);
            throw;
        }
        finally
        {
            if (_inFlight.TryRemove(request.DownloadId, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    private async Task<long> DownloadAttemptAsync(
        DownloadRequest request,
        string partPath,
        bool canResume,
        long currentDownloadedBytes,
        long? totalBytes,
        CancellationToken ct)
    {
        var startingOffset = canResume ? GetFileLength(partPath) : 0;
        if (!canResume && startingOffset > 0)
        {
            DeleteFileIfExists(partPath);
            startingOffset = 0;
            currentDownloadedBytes = 0;
        }

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildDownloadUri(request));
        ApplyBearerToken(requestMessage, request.BearerToken);

        if (canResume && startingOffset > 0)
        {
            requestMessage.Headers.Range = new RangeHeaderValue(startingOffset, null);
        }

        using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct);
        if (startingOffset > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            DeleteFileIfExists(partPath);
            startingOffset = 0;
            currentDownloadedBytes = 0;

            PublishProgress(
                request,
                DownloadStatus.Downloading,
                currentDownloadedBytes,
                totalBytes,
                ProgressMath.CalculatePercent(currentDownloadedBytes, totalBytes),
                null,
                "Server ignored resume request. Restarting from byte 0...");
        }

        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            partPath,
            startingOffset > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        var downloadedBytes = startingOffset;
        var progressTimer = Stopwatch.StartNew();
        var speedTimer = Stopwatch.StartNew();
        long lastSpeedBytes = downloadedBytes;

        if (downloadedBytes > 0 && downloadedBytes != currentDownloadedBytes)
        {
            PublishProgress(
                request,
                DownloadStatus.Downloading,
                downloadedBytes,
                totalBytes,
                ProgressMath.CalculatePercent(downloadedBytes, totalBytes),
                null,
                "Resuming download...");
        }

        while (true)
        {
            var bytesRead = await ReadWithInactivityTimeoutAsync(contentStream, buffer.AsMemory(0, buffer.Length), ct);
            if (bytesRead == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (progressTimer.ElapsedMilliseconds < 250)
            {
                continue;
            }

            var bytesDelta = downloadedBytes - lastSpeedBytes;
            var bytesPerSecond = ProgressMath.CalculateBytesPerSecond(bytesDelta, speedTimer.Elapsed);
            var eta = ProgressMath.CalculateEta(downloadedBytes, totalBytes, bytesPerSecond);
            var percent = ProgressMath.CalculatePercent(downloadedBytes, totalBytes);

            PublishProgress(request, DownloadStatus.Downloading, downloadedBytes, totalBytes, percent, bytesPerSecond, null, eta);

            progressTimer.Restart();
            speedTimer.Restart();
            lastSpeedBytes = downloadedBytes;
        }

        await fileStream.FlushAsync(ct);

        if (totalBytes.HasValue && downloadedBytes < totalBytes.Value)
        {
            throw new IOException($"Download stream ended before completion ({downloadedBytes}/{totalBytes.Value} bytes).");
        }

        var finalBytesDelta = downloadedBytes - lastSpeedBytes;
        var finalBytesPerSecond = ProgressMath.CalculateBytesPerSecond(finalBytesDelta, speedTimer.Elapsed);
        PublishProgress(
            request,
            DownloadStatus.Downloading,
            downloadedBytes,
            totalBytes,
            ProgressMath.CalculatePercent(downloadedBytes, totalBytes),
            finalBytesPerSecond,
            null,
            ProgressMath.CalculateEta(downloadedBytes, totalBytes, finalBytesPerSecond));

        return downloadedBytes;
    }

    private async Task<int> ReadWithInactivityTimeoutAsync(Stream source, Memory<byte> buffer, CancellationToken ct)
    {
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_readInactivityTimeout);

        try
        {
            return await source.ReadAsync(buffer, readCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No download data received for {_readInactivityTimeout.TotalSeconds:F0} seconds.");
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException)
        {
            return !ct.IsCancellationRequested;
        }

        return exception is TimeoutException
            || exception is IOException
            || exception is HttpRequestException;
    }

    private void PublishProgress(
        DownloadRequest request,
        DownloadStatus status,
        long downloaded,
        long? total,
        double? percent,
        double? bytesPerSecond,
        string? message,
        TimeSpan? eta = null)
    {
        _progressSubject.OnNext(new DownloadProgress(
            DownloadId: request.DownloadId,
            RepoId: request.RepoId,
            FileName: request.FileName,
            Status: status,
            BytesDownloaded: downloaded,
            TotalBytes: total,
            Percent: percent,
            BytesPerSecond: bytesPerSecond,
            Eta: eta,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow));
    }

    private static async Task<long?> TryReadTotalBytesAsync(string metaPath, CancellationToken ct)
    {
        var metadata = await ReadLocalMetadataAsync(metaPath, ct);
        return metadata?.TotalBytes;
    }

    private async Task<RemoteMetadata> GetRemoteMetadataAsync(DownloadRequest request, CancellationToken ct)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, BuildDownloadUri(request));
        ApplyBearerToken(headRequest, request.BearerToken);

        using var response = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.MethodNotAllowed || response.StatusCode == HttpStatusCode.NotFound)
        {
            return new RemoteMetadata(
                TotalBytes: response.Content.Headers.ContentLength,
                Etag: response.Headers.ETag?.Tag,
                AcceptRanges: HasByteRanges(response.Headers.AcceptRanges));
        }

        response.EnsureSuccessStatusCode();

        return new RemoteMetadata(
            TotalBytes: response.Content.Headers.ContentLength,
            Etag: response.Headers.ETag?.Tag,
            AcceptRanges: HasByteRanges(response.Headers.AcceptRanges));
    }

    private static Uri BuildDownloadUri(DownloadRequest request)
    {
        var encodedRepo = string.Join('/', request.RepoId.Split('/').Select(Uri.EscapeDataString));
        var encodedFile = string.Join('/', request.FileName.Split('/').Select(Uri.EscapeDataString));
        return new Uri($"https://huggingface.co/{encodedRepo}/resolve/{Uri.EscapeDataString(request.Revision)}/{encodedFile}");
    }

    private static async Task<LocalDownloadMetadata?> ReadLocalMetadataAsync(string metaPath, CancellationToken ct)
    {
        if (!File.Exists(metaPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(metaPath);
        return await JsonSerializer.DeserializeAsync<LocalDownloadMetadata>(stream, cancellationToken: ct);
    }

    private static async Task SaveLocalMetadataAsync(string metaPath, LocalDownloadMetadata metadata, CancellationToken ct)
    {
        await using var stream = File.Create(metaPath);
        await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: ct);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static long GetFileLength(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static void ApplyBearerToken(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static bool HasByteRanges(IEnumerable<string> acceptRanges)
    {
        return acceptRanges.Any(x => x.Equals("bytes", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record RemoteMetadata(long? TotalBytes, string? Etag, bool AcceptRanges);

    private sealed record LocalDownloadMetadata(long? TotalBytes, string? Etag);
}
