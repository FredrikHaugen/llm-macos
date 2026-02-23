using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using Microsoft.Extensions.Logging;

namespace LlmMacos.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        ISettingsService settingsService,
        ISecretStore secretStore,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _logger = logger;
    }

    [ObservableProperty]
    private string hfToken = string.Empty;

    [ObservableProperty]
    private string defaultSystemPrompt = string.Empty;

    [ObservableProperty]
    private int contextSize = 4096;

    [ObservableProperty]
    private int gpuLayerCount = 99;

    [ObservableProperty]
    private double temperature = 0.7d;

    [ObservableProperty]
    private double topP = 0.9d;

    [ObservableProperty]
    private int maxTokens = 512;

    [ObservableProperty]
    private bool useMetal = true;

    [ObservableProperty]
    private string statusMessage = "Settings not loaded.";

    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync(CancellationToken.None);
            var token = await _secretStore.GetHfTokenAsync(CancellationToken.None);

            DefaultSystemPrompt = settings.DefaultSystemPrompt ?? string.Empty;
            ContextSize = (int)settings.DefaultInferenceConfig.ContextSize;
            GpuLayerCount = settings.DefaultInferenceConfig.GpuLayerCount;
            Temperature = settings.DefaultInferenceConfig.Temperature;
            TopP = settings.DefaultInferenceConfig.TopP;
            MaxTokens = settings.DefaultInferenceConfig.MaxTokens;
            UseMetal = settings.DefaultInferenceConfig.UseMetal;
            HfToken = token ?? string.Empty;

            StatusMessage = "Settings loaded.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize settings view model");
            StatusMessage = $"Settings load failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var existing = await _settingsService.LoadAsync(CancellationToken.None);
            var config = new InferenceConfig(
                ContextSize: (uint)Math.Clamp(ContextSize, 512, 32768),
                GpuLayerCount: GpuLayerCount,
                Temperature: (float)Temperature,
                TopP: (float)TopP,
                MaxTokens: MaxTokens,
                UseMetal: UseMetal,
                SystemPrompt: DefaultSystemPrompt);

            var updated = existing with
            {
                DefaultSystemPrompt = DefaultSystemPrompt,
                DefaultInferenceConfig = config
            };

            await _settingsService.SaveAsync(updated, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(HfToken))
            {
                await _secretStore.SetHfTokenAsync(HfToken.Trim(), CancellationToken.None);
            }

            StatusMessage = "Settings saved.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearTokenAsync()
    {
        try
        {
            await _secretStore.ClearHfTokenAsync(CancellationToken.None);
            HfToken = string.Empty;
            StatusMessage = "Hugging Face token removed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear token");
            StatusMessage = $"Token clear failed: {ex.Message}";
        }
    }
}
