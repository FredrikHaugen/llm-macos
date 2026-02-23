using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reactive.Subjects;
using System.Text.Json;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using LlmMacos.Core.Services;

namespace LlmMacos.Infrastructure.Services;

public sealed class DownloadService : IDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly Subject<DownloadProgress> _progressSubject = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new(StringComparer.Ordinal);

    public DownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
            var startingOffset = canResume ? existingBytes : 0;

            if (!canResume && existingBytes > 0)
            {
                DeleteFileIfExists(partPath);
                startingOffset = 0;
            }

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildDownloadUri(request));
            ApplyBearerToken(requestMessage, request.BearerToken);
            if (startingOffset > 0)
            {
                requestMessage.Headers.Range = new RangeHeaderValue(startingOffset, null);
            }

            using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct);
            if (startingOffset > 0 && response.StatusCode == HttpStatusCode.OK)
            {
                startingOffset = 0;
                DeleteFileIfExists(partPath);
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
            var totalBytes = remote.TotalBytes;
            var progressTimer = Stopwatch.StartNew();
            var speedTimer = Stopwatch.StartNew();
            long lastSpeedBytes = downloadedBytes;

            PublishProgress(request.DownloadId, DownloadStatus.Downloading, downloadedBytes, totalBytes, null, null, "Starting download");

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
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

                PublishProgress(request.DownloadId, DownloadStatus.Downloading, downloadedBytes, totalBytes, percent, bytesPerSecond, null, eta);

                progressTimer.Restart();
                speedTimer.Restart();
                lastSpeedBytes = downloadedBytes;
            }

            await fileStream.FlushAsync(ct);
            File.Move(partPath, request.DestinationPath, true);
            DeleteFileIfExists(metaPath);

            var finalPercent = ProgressMath.CalculatePercent(downloadedBytes, totalBytes) ?? 100;
            PublishProgress(request.DownloadId, DownloadStatus.Completed, downloadedBytes, totalBytes, finalPercent, null, "Completed", TimeSpan.Zero);
        }
        catch (OperationCanceledException)
        {
            PublishProgress(request.DownloadId, DownloadStatus.Cancelled, 0, null, null, null, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            PublishProgress(request.DownloadId, DownloadStatus.Failed, 0, null, null, null, ex.Message);
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

    private void PublishProgress(
        string downloadId,
        DownloadStatus status,
        long downloaded,
        long? total,
        double? percent,
        double? bytesPerSecond,
        string? message,
        TimeSpan? eta = null)
    {
        _progressSubject.OnNext(new DownloadProgress(
            DownloadId: downloadId,
            Status: status,
            BytesDownloaded: downloaded,
            TotalBytes: total,
            Percent: percent,
            BytesPerSecond: bytesPerSecond,
            Eta: eta,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow));
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
