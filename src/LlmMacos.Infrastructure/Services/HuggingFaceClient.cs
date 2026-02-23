using System.Net.Http.Headers;
using System.Text.Json;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.Infrastructure.Services;

public sealed class HuggingFaceClient : IHuggingFaceClient
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;

    public HuggingFaceClient(HttpClient httpClient, ISecretStore secretStore)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
    }

    public async Task<IReadOnlyList<ModelSearchResult>> SearchModelsAsync(ModelSearchQuery query, CancellationToken ct)
    {
        var queryString = BuildSearchQueryString(query);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/models{queryString}");
        await AttachTokenAsync(request, ct);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<ModelSearchResult>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var files = ParseFiles(element);
            if (!files.Any(f => f.IsGguf))
            {
                continue;
            }

            results.Add(new ModelSearchResult(
                RepoId: element.GetPropertyOrDefault("id") ?? string.Empty,
                Author: element.GetPropertyOrDefault("author"),
                Description: element.GetPropertyOrDefault("pipeline_tag"),
                Downloads: element.GetInt64OrDefault("downloads"),
                Likes: element.GetInt32OrDefault("likes"),
                LastModified: element.GetDateTimeOffsetOrNull("lastModified"),
                IsPrivate: element.GetBoolOrDefault("private"),
                Files: files));
        }

        return results;
    }

    public async Task<ModelDetails> GetModelDetailsAsync(string repoId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/models/{repoId}");
        await AttachTokenAsync(request, ct);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = document.RootElement;
        var files = ParseFiles(root);

        Dictionary<string, string>? cardData = null;
        if (root.TryGetProperty("cardData", out var cardDataEl) && cardDataEl.ValueKind == JsonValueKind.Object)
        {
            cardData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in cardDataEl.EnumerateObject())
            {
                cardData[prop.Name] = prop.Value.ToString();
            }
        }

        return new ModelDetails(
            RepoId: root.GetPropertyOrDefault("id") ?? repoId,
            Sha: root.GetPropertyOrDefault("sha"),
            LastModified: root.GetPropertyOrDefault("lastModified"),
            IsPrivate: root.GetBoolOrDefault("private"),
            Files: files,
            CardData: cardData);
    }

    public Uri BuildDownloadUri(string repoId, string revision, string fileName)
    {
        var encodedRepo = string.Join('/', repoId.Split('/').Select(Uri.EscapeDataString));
        var encodedFile = string.Join('/', fileName.Split('/').Select(Uri.EscapeDataString));
        return new Uri($"https://huggingface.co/{encodedRepo}/resolve/{Uri.EscapeDataString(revision)}/{encodedFile}");
    }

    private async Task AttachTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _secretStore.GetHfTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static string BuildSearchQueryString(ModelSearchQuery query)
    {
        var parts = new List<string>
        {
            $"limit={Math.Clamp(query.Limit, 1, 100)}",
            $"sort={Uri.EscapeDataString(query.Sort)}",
            "direction=-1",
            "full=true"
        };

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            parts.Add($"search={Uri.EscapeDataString(query.Text)}");
        }

        parts.Add("filter=gguf");

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            parts.Add($"author={Uri.EscapeDataString(query.Author)}");
        }

        if (query.Tags is { Length: > 0 })
        {
            foreach (var tag in query.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                parts.Add($"filter={Uri.EscapeDataString(tag)}");
            }
        }

        return "?" + string.Join("&", parts);
    }

    private static IReadOnlyList<ModelFile> ParseFiles(JsonElement element)
    {
        var files = new List<ModelFile>();

        if (element.TryGetProperty("siblings", out var siblings) && siblings.ValueKind == JsonValueKind.Array)
        {
            foreach (var sibling in siblings.EnumerateArray())
            {
                var name = sibling.GetPropertyOrDefault("rfilename")
                    ?? sibling.GetPropertyOrDefault("filename")
                    ?? sibling.GetPropertyOrDefault("name");

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                files.Add(new ModelFile(
                    Name: name,
                    Size: sibling.GetInt64OrNullable("size"),
                    Sha: sibling.GetPropertyOrDefault("sha"),
                    Rfilename: sibling.GetPropertyOrDefault("rfilename")));
            }
        }

        return files;
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static int GetInt32OrDefault(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return 0;
    }

    public static long GetInt64OrDefault(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return 0L;
    }

    public static long? GetInt64OrNullable(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return null;
    }

    public static bool GetBoolOrDefault(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
        {
            return value.GetBoolean();
        }

        return false;
    }

    public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement element, string name)
    {
        var raw = element.GetPropertyOrDefault(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
