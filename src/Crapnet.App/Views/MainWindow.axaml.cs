using Avalonia.Controls;
using Crapnet.App.ViewModels;

namespace Crapnet.App.Views;

/// <summary>
/// The single window: header, rule list, rule editor and status bar.
/// </summary>
/// <remarks>
/// Crapnet has no dialogs. Everything that would normally be one — profile management, client
/// selection, preflight warnings — is a strip or a flyout inside this window, so that nothing ever
/// blocks the user from reaching for the bypass switch while a test is running.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel)
        : this()
        => DataContext = viewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.InitializeAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Best effort: the process is on its way out, so a failed preference write is not worth
            // holding the shutdown up for.
            _ = viewModel.FlushSettingsAsync();
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
