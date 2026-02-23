using LlmMacos.Core.Abstractions;
using LlmMacos.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlmMacos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLlmMacosInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IAppPathsProvider, AppPathsProvider>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IModelRegistryService, ModelRegistryService>();
        services.AddSingleton<IChatSessionService, ChatSessionService>();
        services.AddSingleton<ISecretStore, MacOsKeychainSecretStore>();
        services.AddSingleton<ILocalFileHasher, LocalFileHasher>();
        services.AddSingleton<IInferenceService, LlamaSharpInferenceService>();

        services.AddHttpClient<IHuggingFaceClient, HuggingFaceClient>(client =>
        {
            client.BaseAddress = new Uri("https://huggingface.co");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LlmMacos/1.0");
        });

        services.AddHttpClient<IDownloadService, DownloadService>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LlmMacos/1.0");
        });

        return services;
    }
}
