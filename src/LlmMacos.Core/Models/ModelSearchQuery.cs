namespace LlmMacos.Core.Models;

public sealed record ModelSearchQuery(
    string? Text,
    int Limit = 20,
    string Sort = "downloads",
    bool IncludePrivate = false,
    string? Author = null,
    string[]? Tags = null);
