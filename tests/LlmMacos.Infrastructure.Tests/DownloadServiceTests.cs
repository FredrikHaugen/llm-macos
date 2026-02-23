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
}
