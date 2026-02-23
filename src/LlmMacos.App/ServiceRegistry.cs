using LlmMacos.Core.Abstractions;
using LlmMacos.Infrastructure;
using LlmMacos.Infrastructure.Services;
using LlmMacos.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LlmMacos.App;

public static class ServiceRegistry
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("Service provider has not been initialized.");

    public static void Initialize()
    {
        if (_services is not null)
        {
            return;
        }

        var collection = new ServiceCollection();

        var pathsProvider = new AppPathsProvider();
        pathsProvider.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(pathsProvider.Paths.Logs, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();

        collection.AddSingleton<IAppPathsProvider>(pathsProvider);
        collection.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
        });

        collection.AddLlmMacosInfrastructure();

        collection.AddSingleton<MainWindowViewModel>();
        collection.AddSingleton<ModelExplorerViewModel>();
        collection.AddSingleton<DownloadsViewModel>();
        collection.AddSingleton<ChatViewModel>();
        collection.AddSingleton<SettingsViewModel>();

        _services = collection.BuildServiceProvider();
    }
}
