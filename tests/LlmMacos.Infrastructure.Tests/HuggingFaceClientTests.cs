using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using LlmMacos.Infrastructure.Services;

namespace LlmMacos.Infrastructure.Tests;

public sealed class HuggingFaceClientTests
{
    [Fact]
    public async Task SearchModelsAsync_FiltersToGgufModels_AndBuildsExpectedQuery()
    {
        var handler = new StubHandler((req, _) =>
        {
            req.RequestUri!.Query.Should().Contain("filter=gguf");
            req.RequestUri!.Query.Should().Contain("search=llama");

            const string payload = """
                                   [
                                     {
                                       "id": "org/model-a",
                                       "author": "org",
                                       "downloads": 100,
                                       "likes": 12,
                                       "private": false,
                                       "siblings": [
                                         { "rfilename": "model-q4.gguf", "size": 123 }
                                       ]
                                     },
                                     {
                                       "id": "org/model-b",
                                       "author": "org",
                                       "downloads": 50,
                                       "likes": 2,
                                       "private": false,
                                       "siblings": [
                                         { "rfilename": "README.md", "size": 10 }
                                       ]
                                     }
                                   ]
                                   """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler, token: null);
        var results = await client.SearchModelsAsync(new ModelSearchQuery("llama", Limit: 20), CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].RepoId.Should().Be("org/model-a");
        results[0].Files.Should().ContainSingle(f => f.IsGguf);
    }

    [Fact]
    public async Task GetModelDetailsAsync_SendsBearerToken_WhenAvailable()
    {
        var handler = new StubHandler((req, _) =>
        {
            req.Headers.Authorization.Should().NotBeNull();
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization!.Parameter.Should().Be("token-123");

            const string payload = """
                                   {
                                     "id": "org/model-a",
                                     "sha": "abc",
                                     "private": false,
                                     "siblings": [
                                       { "rfilename": "model-q4.gguf", "size": 123 }
                                     ]
                                   }
                                   """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler, token: "token-123");
        var details = await client.GetModelDetailsAsync("org/model-a", CancellationToken.None);

        details.RepoId.Should().Be("org/model-a");
        details.Files.Should().ContainSingle(f => f.IsGguf);
    }

    private static HuggingFaceClient CreateClient(HttpMessageHandler handler, string? token)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://huggingface.co")
        };

        ISecretStore secretStore = new TestSecretStore(token);
        return new HuggingFaceClient(httpClient, secretStore);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return responder(request, cancellationToken);
        }
    }

    private sealed class TestSecretStore(string? token) : ISecretStore
    {
        public Task SetHfTokenAsync(string token, CancellationToken ct) => Task.CompletedTask;

        public Task<string?> GetHfTokenAsync(CancellationToken ct) => Task.FromResult(token);

        public Task ClearHfTokenAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
