using Microsoft.Extensions.Logging;

namespace LlmMacos.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        ModelExplorerViewModel modelExplorer,
        ChatViewModel chat,
        SettingsViewModel settings,
        ILogger<MainWindowViewModel> logger)
    {
        ModelExplorer = modelExplorer;
        Chat = chat;
        Settings = settings;
        _logger = logger;

        ModelExplorer.ModelDownloaded += async (_, _) => await Chat.RefreshModelsAsync();
    }

    public ModelExplorerViewModel ModelExplorer { get; }

    public ChatViewModel Chat { get; }

    public SettingsViewModel Settings { get; }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing main window view model");

        await Settings.InitializeAsync();
        await ModelExplorer.InitializeAsync();
        await Chat.InitializeAsync();
    }
}
