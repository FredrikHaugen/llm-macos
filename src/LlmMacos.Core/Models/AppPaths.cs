namespace LlmMacos.Core.Models;

public sealed record AppPaths(
    string Root,
    string Models,
    string Downloads,
    string Chats,
    string Settings,
    string Logs,
    string ModelRegistryFile,
    string SettingsFile);
