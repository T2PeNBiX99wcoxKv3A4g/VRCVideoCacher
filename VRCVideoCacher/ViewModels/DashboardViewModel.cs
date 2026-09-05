using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class MainWindowViewModel;

public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _serverRunning = true;

    [ObservableProperty]
    private string _serverUrl = "http://localhost:9696";

    [ObservableProperty]
    private long _totalCacheSize;

    [ObservableProperty]
    private float _maxCacheSize;

    [ObservableProperty]
    private int _cachedVideoCount;

    [ObservableProperty]
    private int _downloadQueueCount;

    [ObservableProperty]
    private string _cookieStatus = Localizer.Get("NotSet");

    [ObservableProperty]
    private string _currentDownloadText = Localizer.Get("None");

    [ObservableProperty]
    private bool _hostState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMotd))]
    private string? _motd;

    [ObservableProperty]
    private bool _cookiesFileExists = false;

    public bool HasMotd => !string.IsNullOrWhiteSpace(Motd);

    // Required-tools verification (present AND functioning).
    private readonly ToolStatusItem _ytdlpTool = new("yt-dlp");
    private readonly ToolStatusItem _ffmpegTool = new("FFmpeg");
    private readonly ToolStatusItem _denoTool = new("Deno");
    private readonly ToolStatusItem _potTool = new("PO Token Provider");
    private readonly ToolStatusItem _opusTool = new("Opus-in-MP4 Codec");

    public ObservableCollection<ToolStatusItem> Tools { get; }

    public DashboardViewModel()
    {
        ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
        MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        HostState = ElevatorManager.HasHostsLine;

        Tools = [_ytdlpTool, _ffmpegTool, _denoTool, _potTool, _opusTool];

        // Initial data load
        RefreshData();

        Motd = VvcConfigService.CurrentConfig.Motd;

        // Subscribe to language changes to refresh localized strings
        Localizer.LanguageChanged += (_, _) => Dispatcher.UIThread.InvokeAsync(RefreshLocalizedStrings);

        // Subscribe to events
        CacheManager.OnCacheChanged += OnCacheChanged;
        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        ConfigManager.OnConfigChanged += OnConfigChanged;
        Program.OnCookiesUpdated += OnCookiesUpdated;
        VvcConfigService.OnApiConfigChanged += OnApiConfigChanged;
    }

    private void RefreshLocalizedStrings()
    {
        // Force BoolToStatusConverter to re-evaluate with new language
        OnPropertyChanged(nameof(ServerRunning));

        // Refresh directly-assigned localized strings
        if (VideoDownloader.GetCurrentDownload() == null)
            CurrentDownloadText = Localizer.Get("None");
    }

    private void OnCookiesUpdated()
    {
        _ = ValidateCookiesAsync();
    }

    private void OnApiConfigChanged()
    {
        // Fires on the hourly YtdlUpdaterTask thread. Setting Motd now builds HyperlinkButton controls
        // (MarkdownText renders it as inlines), so this must happen on the UI thread.
        Dispatcher.UIThread.InvokeAsync(() => Motd = VvcConfigService.CurrentConfig.Motd);
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCacheStats);
    }

    private void OnDownloadStarted(Models.VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = $"{video.UrlType}: {video.VideoId}";
        });
    }

    private void OnDownloadCompleted(Models.VideoInfo video, bool success)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = Localizer.Get("None");
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadQueueCount = VideoDownloader.GetQueueCount();
        });
    }

    private void OnConfigChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
            MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        });
        _ = ValidateCookiesAsync();
    }

    [RelayCommand]
    private void RefreshData()
    {
        RefreshCacheStats();
        DownloadQueueCount = VideoDownloader.GetQueueCount();

        var currentDownload = VideoDownloader.GetCurrentDownload();
        CurrentDownloadText = currentDownload != null
            ? $"{currentDownload.UrlType}: {currentDownload.VideoId}"
            : Localizer.Get("None");

        _ = ValidateCookiesAsync();
        _ = VerifyTools();
    }

    [RelayCommand]
    private async Task VerifyTools()
    {
        foreach (var tool in Tools)
            tool.State = ToolState.Checking;

        Apply(_ytdlpTool, await ToolVerifier.VerifyYtDlpAsync());
        Apply(_ffmpegTool, await ToolVerifier.VerifyFfmpegAsync());
        Apply(_denoTool, await ToolVerifier.VerifyDenoAsync());

        // The PO token provider is required even with SABR streaming turned off — the legacy yt-dlp path
        // sends the same GVS token — so it is always verified, never shown as "disabled".
        var pot = await ToolVerifier.VerifyPotProviderAsync();
        _potTool.State = pot.Ok ? ToolState.Ok : ToolState.Failed;
        _potTool.Detail = pot.Ok ? string.Empty : Localizer.Get("ToolNotWorking");

        await VerifyOpusCodecAsync();
    }

    private static void Apply(ToolStatusItem tool, ToolCheck check)
    {
        tool.State = check.Ok ? ToolState.Ok : ToolState.Failed;
        tool.Detail = check.Ok
            ? check.Detail
            : Localizer.Get(check.Present ? "ToolNotWorking" : "ToolNotFound");
    }

    private async Task VerifyOpusCodecAsync()
    {
        // Windows-only decode capability. Non-Windows (and "not yet probed") is not a failure.
        if (!OperatingSystem.IsWindows())
        {
            _opusTool.State = ToolState.NotApplicable;
            _opusTool.Detail = Localizer.Get("ToolNotApplicable");
            return;
        }

        // Wait for the (one-shot) decode probe rather than reading a null result the startup probe hasn't
        // finished populating yet — that was the "N/A until reverify" glitch.
        await OpusMp4Check.EnsureAsync();

        switch (OpusMp4Check.Supported)
        {
            case true:
                _opusTool.State = ToolState.Ok;
                _opusTool.Detail = string.Empty;
                break;
            case false:
                // Soft fallback: playback still works via AAC, so this is a warning, not a hard failure.
                _opusTool.State = ToolState.Warning;
                _opusTool.Detail = Localizer.Get("ToolAacFallback");
                break;
            default:
                _opusTool.State = ToolState.NotApplicable;
                _opusTool.Detail = Localizer.Get("ToolNotApplicable");
                break;
        }
    }

    [RelayCommand]
    private void ToggleHost()
    {
        ElevatorManager.ToggleHostLine();
        Dispatcher.UIThread.Post(() => { HostState = ElevatorManager.HasHostsLine; });
    }

    private void RefreshCacheStats()
    {
        TotalCacheSize = CacheManager.GetTotalCacheSize();
        // Subtract 1 for index.html if it exists in the cache
        var count = CacheManager.GetCachedVideoCount();
        var assets = CacheManager.GetCachedAssets();
        if (assets.ContainsKey("index.html"))
            count--;
        CachedVideoCount = count;
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start("explorer.exe", cachePath);
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }

    private async Task ValidateCookiesAsync()
    {
        CookiesFileExists = Program.DoesCookieFileExist();

        if (!Program.IsCookiesEnabledAndValid())
        {
            Dispatcher.UIThread.Post(() => CookieStatus = Localizer.Get("NotSet"));
            return;
        }

        Dispatcher.UIThread.Post(() => CookieStatus = Localizer.Get("Checking"));

        var result = await Program.ValidateCookiesAsync();
        Dispatcher.UIThread.Post(() =>
        {
            CookieStatus = result switch
            {
                true => Localizer.Get("Valid"),
                false => Localizer.Get("Expired"),
                null => Localizer.Get("Unknown")
            };
        });
    }

    [RelayCommand]
    private async Task SetupCookieExtension()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new CookieSetupViewModel();
            var window = new CookieSetupWindow
            {
                DataContext = viewModel
            };

            viewModel.RequestClose += () => window.Close();

            await window.ShowDialog(desktop.MainWindow!);

            // Refresh cookies status after dialog closes
            _ = ValidateCookiesAsync();
        }
    }

    [RelayCommand]
    private async Task ClearCookies()
    {
        Program.DeleteCookieFile();
        await ValidateCookiesAsync();
    }
}
