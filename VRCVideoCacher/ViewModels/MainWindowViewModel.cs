using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string StatusNormalColor = "#CCCCCC";
    private const string StatusWarningColor = "#FFB74D";

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _statusText = Localizer.Get("ServerRunning");

    [ObservableProperty]
    private string _statusColor = StatusNormalColor;

    [ObservableProperty]
    private bool _statusShowBar;

    [ObservableProperty]
    private bool _statusIndeterminate;

    [ObservableProperty]
    private double _statusProgress;

    [ObservableProperty]
    private string _cacheStatusText = "Cache: 0 B";

    [ObservableProperty]
    private string _title = $"VRCVideoCacher v{Program.Version}";

    public DashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }
    public CacheBrowserViewModel CacheBrowser { get; }
    public DownloadQueueViewModel DownloadQueue { get; }
    public LogViewerViewModel LogViewer { get; }
    public HistoryViewModel History { get; }
    public AboutViewModel About { get; }

    public MainWindowViewModel()
    {
        Dashboard = new();
        Settings = new();
        CacheBrowser = new();
        DownloadQueue = new();
        LogViewer = new();
        History = new();
        About = new();

        _currentView = Dashboard;

        // Subscribe to cache changes for status bar
        CacheManager.OnCacheChanged += (_, _) => UpdateCacheStatus();
        UpdateCacheStatus();

        // Live activity status (downloads, streaming, provisioning, warnings).
        StatusService.Changed += OnStatusChanged;
        OnStatusChanged();

        // Re-render the (localized) status text when the language changes.
        Localizer.LanguageChanged += (_, _) => OnStatusChanged();
    }

    private void OnStatusChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var snapshot = StatusService.Current;
            if (!snapshot.IsBusy)
            {
                StatusText = Localizer.Get("ServerRunning");
                StatusColor = StatusNormalColor;
                StatusShowBar = false;
                StatusIndeterminate = false;
                StatusProgress = 0;
                return;
            }

            var text = snapshot.Text;
            if (snapshot.ExtraCount > 0)
                text += string.Format(Localizer.Get("StatusMore"), snapshot.ExtraCount);

            StatusText = text;
            StatusColor = snapshot.Level == StatusLevel.Warning ? StatusWarningColor : StatusNormalColor;
            StatusShowBar = snapshot.ShowBar;
            StatusIndeterminate = snapshot.Indeterminate;
            StatusProgress = snapshot.Progress;
        });
    }

    private void UpdateCacheStatus()
    {
        var size = CacheManager.GetTotalCacheSize();
        var maxSize = ConfigManager.Config.CacheMaxSizeInGb;

        if (maxSize > 0)
        {
            var maxBytes = (long)(maxSize * 1024 * 1024 * 1024);
            CacheStatusText = $"Cache: {FormatSize(size)} / {FormatSize(maxBytes)}";
        }
        else
        {
            CacheStatusText = $"Cache: {FormatSize(size)}";
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        if (bytes == 0) return "0 B";
        var mag = (int)Math.Log(bytes, 1024);
        var adjustedSize = bytes / Math.Pow(1024, mag);
        return $"{adjustedSize:N2} {suffixes[mag]}";
    }

    [RelayCommand]
    private void NavigateToDashboard() => CurrentView = Dashboard;

    [RelayCommand]
    private void NavigateToSettings() => CurrentView = Settings;

    [RelayCommand]
    private void NavigateToCacheBrowser() => CurrentView = CacheBrowser;

    [RelayCommand]
    private void NavigateToDownloadQueue() => CurrentView = DownloadQueue;

    [RelayCommand]
    private void NavigateToLogViewer() => CurrentView = LogViewer;

    [RelayCommand]
    private void NavigateToHistory() => CurrentView = History;

    [RelayCommand]
    public void NavigateToAbout() => CurrentView = About;
}
