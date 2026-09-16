using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Crapnet.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Crapnet.App;

/// <summary>
/// The Avalonia application: theme, styles and the one window.
/// </summary>
/// <remarks>
/// The container is handed in by <c>Program</c> rather than built here, so that the XAML previewer
/// can load this type without dragging the Windows-only adapters in behind it.
/// </remarks>
// Fully qualified: the root namespace Crapnet.App sits next to Crapnet.Application, so a bare
// "Application" binds to that namespace rather than to Avalonia's type.
public sealed class App : Avalonia.Application
{
    /// <summary>Set before the application starts. Null under the previewer.</summary>
    public IServiceProvider? Services { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
