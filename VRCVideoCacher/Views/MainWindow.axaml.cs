using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using VRCVideoCacher.Utils;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        InitializeDebugPage();
#endif
        Opened += OnWindowOpened;
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
    }

#if DEBUG
    private void InitializeDebugPage()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("26, *")
        };

        var icon = new MaterialIcon
        {
            Kind = MaterialIconKind.BugOutline,
            Width = 19,
            Height = 19,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(icon, 0);

        var label = new TextBlock
        {
            Text = "Debug",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(label, 1);

        grid.Children.Add(icon);
        grid.Children.Add(label);

        var button = new Button
        {
            Content = grid
        };
        button.Bind(Button.CommandProperty, new Binding("NavigateToDebugCommand"));
        button.Classes.Add("nav-button");
        NavItemsPanel.Children.Add(button);

        ContentHost.DataTemplates.Add(new FuncDataTemplate<DebugViewModel>((_, _) => new DebugView()));
    }
#endif

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is { } ex)
            LoggerUtils.LogUnhandledException(ex, "Unhandled UI thread exception");
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Only run once
        Opened -= OnWindowOpened;

        // Delay slightly to let the main window fully render
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(500);
            Program.InitializeUIBackend();
        });

        // Check if we should show the cookie setup wizard
        // Show if: cookies are enabled, setup not completed, and cookies not already valid
        if (ConfigManager.Config.YtdlpUseCookies &&
            !ConfigManager.Config.CookieSetupCompleted &&
            !Program.IsCookiesEnabledAndValid())
            // Delay slightly to let the main window fully render
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(500);
                await ShowCookieSetupDialog();
            });
    }

    private async Task ShowCookieSetupDialog()
    {
        var viewModel = new CookieSetupViewModel();
        var window = new CookieSetupWindow
        {
            DataContext = viewModel
        };

        viewModel.RequestClose += () => window.Close();

        await window.ShowDialog(this);
    }
}