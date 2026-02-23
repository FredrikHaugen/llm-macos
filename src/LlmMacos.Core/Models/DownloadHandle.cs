namespace LlmMacos.Core.Models;

public sealed record DownloadHandle(
    string DownloadId,
    Task Completion);
