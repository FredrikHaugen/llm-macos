using Avalonia;

namespace LlmMacos.App;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ServiceRegistry.Initialize();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
