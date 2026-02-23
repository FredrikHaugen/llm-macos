using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LlmMacos.App.Models;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;
using Microsoft.Extensions.Logging;

namespace LlmMacos.App.ViewModels;

public sealed partial class ChatViewModel : ViewModelBase
{
    private readonly IModelRegistryService _modelRegistryService;
    private readonly IInferenceService _inferenceService;
    private readonly IChatSessionService _chatSessionService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ChatViewModel> _logger;

    private CancellationTokenSource? _generationCts;
    private ChatSession? _currentSession;
    private AppSettings _settings = AppSettings.Default;

    public ChatViewModel(
        IModelRegistryService modelRegistryService,
        IInferenceService inferenceService,
        IChatSessionService chatSessionService,
        ISettingsService settingsService,
        ILogger<ChatViewModel> logger)
    {
        _modelRegistryService = modelRegistryService;
        _inferenceService = inferenceService;
        _chatSessionService = chatSessionService;
        _settingsService = settingsService;
        _logger = logger;

        AvailableModels = [];
        Messages = [];
    }

    public ObservableCollection<LocalModel> AvailableModels { get; }

    public ObservableCollection<ChatMessageItem> Messages { get; }

    [ObservableProperty]
    private LocalModel? selectedModel;

    [ObservableProperty]
    private string inputText = string.Empty;

    [ObservableProperty]
    private bool isGenerating;

    [ObservableProperty]
    private string statusMessage = "Load a downloaded model to begin chatting.";

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync(CancellationToken.None);
        await RefreshModelsAsync();

        if (!string.IsNullOrWhiteSpace(_settings.LastModelId))
        {
            SelectedModel = AvailableModels.FirstOrDefault(m => m.ModelId.Equals(_settings.LastModelId, StringComparison.Ordinal));
        }

        await RestoreLatestSessionForModelAsync(SelectedModel);
    }

    [RelayCommand]
    public async Task RefreshModelsAsync()
    {
        await _modelRegistryService.ReconcileAsync(CancellationToken.None);
        var models = await _modelRegistryService.ListAsync(CancellationToken.None);

        AvailableModels.Clear();
        foreach (var model in models)
        {
            AvailableModels.Add(model);
        }

        if (SelectedModel is null)
        {
            SelectedModel = AvailableModels.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task LoadSelectedModelAsync()
    {
        if (SelectedModel is null)
        {
            StatusMessage = "Select a downloaded model first.";
            return;
        }

        try
        {
            var modelToLoad = SelectedModel;
            await _inferenceService.LoadModelAsync(
                modelToLoad,
                _settings.DefaultInferenceConfig with { SystemPrompt = _settings.DefaultSystemPrompt },
                CancellationToken.None);

            var updatedModel = modelToLoad with
            {
                State = ModelState.Loaded,
                LastUsedAt = DateTimeOffset.UtcNow
            };
            await _modelRegistryService.UpsertAsync(updatedModel, CancellationToken.None);

            var index = AvailableModels.IndexOf(modelToLoad);
            if (index >= 0)
            {
                AvailableModels[index] = updatedModel;
            }

            SelectedModel = updatedModel;
            StatusMessage = $"Loaded model: {updatedModel.RepoId}";

            _settings = _settings with { LastModelId = updatedModel.ModelId };
            await _settingsService.SaveAsync(_settings, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model load failed");
            StatusMessage = $"Load failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NewSessionAsync()
    {
        if (SelectedModel is null)
        {
            StatusMessage = "Choose a model before creating a chat session.";
            return;
        }

        _currentSession = await _chatSessionService.CreateAsync(SelectedModel.ModelId, CancellationToken.None);
        Messages.Clear();
        StatusMessage = "New chat session started.";
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsGenerating || string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }

        if (!_inferenceService.IsLoaded)
        {
            StatusMessage = "Load a model first.";
            return;
        }

        if (_currentSession is null)
        {
            await NewSessionAsync();
        }

        if (_currentSession is null)
        {
            StatusMessage = "Failed to start session.";
            return;
        }

        var userText = InputText.Trim();
        InputText = string.Empty;

        var userMessage = new ChatMessageItem
        {
            Role = "user",
            Content = userText,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Messages.Add(userMessage);
        var historyBeforeUserMessage = _currentSession.Messages.ToList();

        var assistantMessage = new ChatMessageItem
        {
            Role = "assistant",
            Content = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Messages.Add(assistantMessage);
        IsGenerating = true;
        StatusMessage = "Generating...";

        _generationCts = new CancellationTokenSource();

        try
        {
            var turn = new ChatTurn(
                UserMessage: userText,
                History: historyBeforeUserMessage,
                Config: _settings.DefaultInferenceConfig with { SystemPrompt = _settings.DefaultSystemPrompt });

            var sb = new StringBuilder();
            await foreach (var chunk in _inferenceService.StreamReplyAsync(turn, _generationCts.Token))
            {
                if (chunk.IsCompleted)
                {
                    break;
                }

                sb.Append(chunk.Text);
                var current = sb.ToString();

                Dispatcher.UIThread.Post(() =>
                {
                    assistantMessage.Content = current;
                });
            }

            var final = sb.ToString();
            _currentSession.Messages.Add(new ChatMessage("user", userText, userMessage.CreatedAt));
            _currentSession.Messages.Add(new ChatMessage("assistant", final, assistantMessage.CreatedAt));
            await _chatSessionService.SaveAsync(_currentSession, CancellationToken.None);

            StatusMessage = "Response complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generation stopped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generation failed");
            StatusMessage = $"Generation failed: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    [RelayCommand]
    private void StopGenerating()
    {
        _generationCts?.Cancel();
    }

    partial void OnSelectedModelChanged(LocalModel? value)
    {
        _ = RestoreLatestSessionForModelAsync(value);
    }

    private async Task RestoreLatestSessionForModelAsync(LocalModel? model)
    {
        if (model is null)
        {
            return;
        }

        try
        {
            var summaries = await _chatSessionService.ListAsync(CancellationToken.None);
            var latest = summaries.FirstOrDefault(s => s.ModelId.Equals(model.ModelId, StringComparison.Ordinal));
            if (latest is null)
            {
                return;
            }

            var session = await _chatSessionService.GetAsync(latest.SessionId, CancellationToken.None);
            if (session is null)
            {
                return;
            }

            _currentSession = session;
            Messages.Clear();
            foreach (var message in session.Messages)
            {
                Messages.Add(new ChatMessageItem
                {
                    Role = message.Role,
                    Content = message.Content,
                    CreatedAt = message.CreatedAt
                });
            }

            StatusMessage = $"Restored chat: {session.Title}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore session for model {ModelId}", model.ModelId);
        }
    }
}
