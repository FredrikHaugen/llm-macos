using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.Infrastructure.Services;

public sealed class AppPathsProvider : IAppPathsProvider
{
    public AppPathsProvider()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(home, "Library", "Application Support", "LlmMacos");

        var models = Path.Combine(root, "models");
        var downloads = Path.Combine(root, "downloads");
        var chats = Path.Combine(root, "chats");
        var settings = Path.Combine(root, "settings");
        var logs = Path.Combine(root, "logs");

        Paths = new AppPaths(
            Root: root,
            Models: models,
            Downloads: downloads,
            Chats: chats,
            Settings: settings,
            Logs: logs,
            ModelRegistryFile: Path.Combine(root, "model-registry.json"),
            SettingsFile: Path.Combine(settings, "settings.json"));
    }

    public AppPaths Paths { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Paths.Root);
        Directory.CreateDirectory(Paths.Models);
        Directory.CreateDirectory(Paths.Downloads);
        Directory.CreateDirectory(Paths.Chats);
        Directory.CreateDirectory(Paths.Settings);
        Directory.CreateDirectory(Paths.Logs);
    }
}
