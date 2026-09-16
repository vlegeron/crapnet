using Avalonia;
using Crapnet.App.Composition;

namespace Crapnet.App;

internal static class Program
{
    /// <summary>
    /// STA is required: the hotspot and Internet Connection Sharing adapters both go through COM.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        using var services = CompositionRoot.Build();
        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Entry point the XAML previewer looks for. It runs without a container.</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? services)
        => AppBuilder.Configure(() => new App { Services = services })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
