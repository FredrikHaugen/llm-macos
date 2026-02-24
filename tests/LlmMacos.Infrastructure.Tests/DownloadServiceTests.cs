using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LlmMacos.Core.Models;
using LlmMacos.Infrastructure.Services;

namespace LlmMacos.Infrastructure.Tests;

public sealed class DownloadServiceTests
{
    [Fact]
    public async Task QueueAsync_ResumesFromPartialFile_WhenRangeSupported()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new RangeAwareHandler(payload);
        var sut = new DownloadService(new HttpClient(handler));

        var tempRoot = Path.Combine(Path.GetTempPath(), "llm-macos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var destination = Path.Combine(tempRoot, "model.gguf");
            var part = destination + ".part";
            var meta = destination + ".part.meta";

            await File.WriteAllBytesAsync(part, payload[..10]);
            await File.WriteAllTextAsync(meta, JsonSerializer.Serialize(new { TotalBytes = payload.Length, Etag = "\"v1\"" }));

            var request = new DownloadRequest(
                DownloadId: "dl-1",
                RepoId: "org/model",
                Revision: "main",
                FileName: "model.gguf",
                DestinationPath: destination,
                BearerToken: null);

            var handle = await sut.QueueAsync(request, CancellationToken.None);
            await handle.Completion;

            handler.LastRangeStart.Should().Be(10);
            File.Exists(destination).Should().BeTrue();
            File.Exists(part).Should().BeFalse();
            File.Exists(meta).Should().BeFalse();

            var downloaded = await File.ReadAllBytesAsync(destination);
            downloaded.Should().Equal(payload);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task QueueAsync_EmitsProgressWhenTotalBytesUnknown()
    {
        var payload = Encoding.UTF8.GetBytes("unknown-size-payload");
        var handler = new UnknownLengthHandler(payload);
        var sut = new DownloadService(new HttpClient(handler));

        var progressEvents = new List<DownloadProgress>();
        using var subscription = sut.ProgressStream.Subscribe(new ProgressObserver(progressEvents.Add));

        var tempRoot = Path.Combine(Path.GetTempPath(), "llm-macos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var destination = Path.Combine(tempRoot, "model.gguf");
            var request = new DownloadRequest(
                DownloadId: "dl-unknown",
                RepoId: "org/model",
                Revision: "main",
                FileName: "model.gguf",
                DestinationPath: destination,
                BearerToken: null);

            var handle = await sut.QueueAsync(request, CancellationToken.None);
            await handle.Completion;

            progressEvents.Any(e => e.Status == DownloadStatus.Downloading && e.TotalBytes == null).Should().BeTrue();
            progressEvents.Any(e => e.Status == DownloadStatus.Completed && e.BytesDownloaded == payload.Length).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task QueueAsync_RetriesAndResumes_WhenStreamStalls()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz0123456789");
        var handler = new StallThenResumeHandler(payload, stallAfterBytes: 8, stallDuration: TimeSpan.FromMilliseconds(250));
        var sut = new DownloadService(
            new HttpClient(handler),
            readInactivityTimeout: TimeSpan.FromMilliseconds(40),
            maxRetryAttempts: 3,
            retryDelay: TimeSpan.Zero);

        var progressEvents = new List<DownloadProgress>();
        using var subscription = sut.ProgressStream.Subscribe(new ProgressObserver(progressEvents.Add));

        var tempRoot = Path.Combine(Path.GetTempPath(), "llm-macos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var destination = Path.Combine(tempRoot, "model.gguf");
            var request = new DownloadRequest(
                DownloadId: "dl-stall",
                RepoId: "org/model",
                Revision: "main",
                FileName: "model.gguf",
                DestinationPath: destination,
                BearerToken: null);

            var handle = await sut.QueueAsync(request, CancellationToken.None);
            await handle.Completion;

            handler.GetRangeStarts.Should().ContainInOrder((long?)null, 8L);
            progressEvents.Any(e => e.Message?.Contains("retrying", StringComparison.OrdinalIgnoreCase) == true).Should().BeTrue();
            progressEvents.Any(e => e.Status == DownloadStatus.Completed).Should().BeTrue();

            var downloaded = await File.ReadAllBytesAsync(destination);
            downloaded.Should().Equal(payload);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private sealed class RangeAwareHandler(byte[] payload) : HttpMessageHandler
    {
        public long? LastRangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };

                head.Content.Headers.ContentLength = payload.Length;
                head.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                head.Headers.AcceptRanges.Add("bytes");
                return Task.FromResult(head);
            }

            if (request.Method == HttpMethod.Get)
            {
                var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
                LastRangeStart = from;

                if (from.HasValue)
                {
                    var data = payload[(int)from.Value..];
                    var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(data)
                    };

                    partial.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                    partial.Content.Headers.ContentLength = data.Length;
                    partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from.Value, payload.Length - 1, payload.Length);
                    return Task.FromResult(partial);
                }

                var ok = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                };

                ok.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                ok.Content.Headers.ContentLength = payload.Length;
                return Task.FromResult(ok);
            }

            throw new InvalidOperationException("Unexpected HTTP method");
        }
    }

    private sealed class UnknownLengthHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new UnknownLengthContent(Array.Empty<byte>())
                };

                head.Headers.AcceptRanges.Add("bytes");
                return Task.FromResult(head);
            }

            if (request.Method == HttpMethod.Get)
            {
                var ok = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new UnknownLengthContent(payload)
                };

                return Task.FromResult(ok);
            }

            throw new InvalidOperationException("Unexpected HTTP method");
        }
    }

    private sealed class StallThenResumeHandler(byte[] payload, int stallAfterBytes, TimeSpan stallDuration) : HttpMessageHandler
    {
        private int _getCount;

        public List<long?> GetRangeStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };

                head.Content.Headers.ContentLength = payload.Length;
                head.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                head.Headers.AcceptRanges.Add("bytes");
                return Task.FromResult(head);
            }

            if (request.Method == HttpMethod.Get)
            {
                _getCount++;
                var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
                GetRangeStarts.Add(from);

                if (_getCount == 1)
                {
                    var stalled = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new StallingReadStream(payload, stallAfterBytes, stallDuration))
                    };

                    stalled.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                    stalled.Content.Headers.ContentLength = payload.Length;
                    return Task.FromResult(stalled);
                }

                var start = (int)(from ?? 0);
                var data = payload[start..];
                var response = new HttpResponseMessage(from.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(data)
                };

                response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                response.Content.Headers.ContentLength = data.Length;
                if (from.HasValue)
                {
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from.Value, payload.Length - 1, payload.Length);
                }

                return Task.FromResult(response);
            }

            throw new InvalidOperationException("Unexpected HTTP method");
        }
    }

    private sealed class ProgressObserver(Action<DownloadProgress> onNext) : IObserver<DownloadProgress>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DownloadProgress value)
        {
            onNext(value);
        }
    }

    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(payload, 0, payload.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StallingReadStream : MemoryStream
    {
        private readonly int _stallAfterBytes;
        private readonly TimeSpan _stallDuration;
        private bool _stalled;

        public StallingReadStream(byte[] buffer, int stallAfterBytes, TimeSpan stallDuration)
            : base(buffer, writable: false)
        {
            _stallAfterBytes = stallAfterBytes;
            _stallDuration = stallDuration;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_stalled && Position < _stallAfterBytes)
            {
                var remaining = (int)(_stallAfterBytes - Position);
                var sliceLength = Math.Min(buffer.Length, Math.Max(remaining, 1));
                return await base.ReadAsync(buffer[..sliceLength], cancellationToken);
            }

            _stalled = true;
            await Task.Delay(_stallDuration, cancellationToken);
            return 0;
        }
    }
}
