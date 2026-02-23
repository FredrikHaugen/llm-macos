using System.Text;
using LLama;
using LLama.Common;
using LlmMacos.Core.Abstractions;
using LlmMacos.Core.Models;

namespace LlmMacos.Infrastructure.Services;

public sealed class LlamaSharpInferenceService : IInferenceService
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;

    public LocalModel? ActiveModel { get; private set; }

    public bool IsLoaded => _executor is not null;

    public async Task LoadModelAsync(LocalModel model, InferenceConfig config, CancellationToken ct)
    {
        if (!File.Exists(model.LocalPath))
        {
            throw new FileNotFoundException("Model file does not exist.", model.LocalPath);
        }

        if (!model.LocalPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only GGUF models are supported.");
        }

        await _stateLock.WaitAsync(ct);
        try
        {
            await UnloadCoreAsync();

            var modelParams = new ModelParams(model.LocalPath)
            {
                ContextSize = config.ContextSize,
                GpuLayerCount = config.UseMetal ? config.GpuLayerCount : 0
            };

            try
            {
                _weights = LLamaWeights.LoadFromFile(modelParams);
                _context = _weights.CreateContext(modelParams);
            }
            catch when (config.UseMetal)
            {
                // Automatic fallback to CPU when GPU init is unavailable.
                modelParams.GpuLayerCount = 0;
                _weights = LLamaWeights.LoadFromFile(modelParams);
                _context = _weights.CreateContext(modelParams);
            }

            _executor = new InteractiveExecutor(_context);
            ActiveModel = model with { State = ModelState.Loaded, LastUsedAt = DateTimeOffset.UtcNow };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async IAsyncEnumerable<TokenChunk> StreamReplyAsync(ChatTurn turn, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        InteractiveExecutor executor;
        await _stateLock.WaitAsync(ct);
        try
        {
            if (_executor is null)
            {
                throw new InvalidOperationException("No model loaded.");
            }

            executor = _executor;
        }
        finally
        {
            _stateLock.Release();
        }

        var prompt = BuildPrompt(turn);
        var inferenceParams = new InferenceParams
        {
            Temperature = turn.Config.Temperature,
            TopP = turn.Config.TopP,
            MaxTokens = turn.Config.MaxTokens,
            AntiPrompts = ["\nUser:"]
        };

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
        {
            yield return new TokenChunk(Text: token);
        }

        yield return new TokenChunk(Text: string.Empty, IsCompleted: true, FinishReason: "stop");
    }

    public async Task UnloadAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            await UnloadCoreAsync();
            ActiveModel = null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private Task UnloadCoreAsync()
    {
        _executor = null;

        _context?.Dispose();
        _context = null;

        _weights?.Dispose();
        _weights = null;

        return Task.CompletedTask;
    }

    private static string BuildPrompt(ChatTurn turn)
    {
        var sb = new StringBuilder();
        var systemPrompt = turn.Config.SystemPrompt ?? "You are a helpful assistant.";

        sb.AppendLine($"System: {systemPrompt}");
        foreach (var message in turn.History)
        {
            var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? "Assistant"
                : "User";
            sb.AppendLine($"{role}: {message.Content}");
        }

        sb.AppendLine($"User: {turn.UserMessage}");
        sb.Append("Assistant: ");
        return sb.ToString();
    }
}
