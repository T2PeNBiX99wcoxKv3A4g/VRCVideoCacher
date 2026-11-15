using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.API;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.ViewModels;

public record LanguageOption(string Code, string DisplayName);

public partial class BlockedUrlEntry(string url) : ObservableObject
{
    [ObservableProperty] public partial string Url { get; set; } = url;
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public partial class RedirectUrlEntry(string url, string redirectUrl) : ObservableObject
{
    [ObservableProperty] public partial string Url { get; set; } = url;

    [ObservableProperty] public partial string RedirectUrl { get; set; } = redirectUrl;
}

[ConfigBindings(typeof(ConfigModel))]
public partial class SettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;

    public SettingsViewModel()
    {
        BlockedUrls.CollectionChanged += OnBlockedUrlsCollectionChanged;
        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();
    }


    // Server Settings
    [ObservableProperty] public partial string WebServerUrl { get; set; } = string.Empty;

    // Download Settings
    [ObservableProperty] public partial bool YtdlUseCookies { get; set; }

    [ObservableProperty] public partial bool YtdlAutoUpdate { get; set; }

    [ObservableProperty] public partial string YtdlAdditionalArgs { get; set; } = string.Empty;

    [ObservableProperty] public partial string YtdlDubLanguage { get; set; } = string.Empty;

    [ObservableProperty] public partial string GitHubToken { get; set; } = string.Empty;

    // Codec filters
    [ObservableProperty] public partial bool YtdlAllowAV1 { get; set; }

    // ComboBox index: 0 = Auto, 1 = Allow, 2 = Block
    [ConfigBindingIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVp9AmdHint))]
    public partial int YtdlVp9Mode { get; set; }

    public bool ShowVp9AmdHint => YtdlVp9Mode == 0 && GpuCheck.HasAmdGpu;

    [ObservableProperty] public partial int YouTubeMaxResolution { get; set; }

    // Resolution options for the dropdown, shared by streaming and caching.
    public int[] ResolutionOptions { get; } = [144, 240, 360, 480, 720, 1080, 1440, 2160];

    // YouTube SABR Options
    [ConfigBindingIgnore]
    [ObservableProperty] public partial bool PreferSabrStreaming { get; set; }

    [ObservableProperty] public partial bool SabrFilterDrcAudio { get; set; }
    [ObservableProperty] public partial bool SabrFilterSuperResolution { get; set; }

    [ObservableProperty] public partial bool SabrFilterVoiceBoostedAudio { get; set; }

    [ObservableProperty] public partial string SabrAdditionalArgs { get; set; } = string.Empty;

    // Cache Settings
    [ObservableProperty] public partial string CachedAssetPath { get; set; } = string.Empty;

    [ObservableProperty] public partial bool CacheYouTube { get; set; }

    [ObservableProperty] public partial int CacheYouTubeMaxLength { get; set; }

    [ObservableProperty] public partial float CacheMaxSizeInGb { get; set; }

    [ObservableProperty] public partial bool CachePyPyDance { get; set; }

    [ObservableProperty] public partial bool CacheVRDancing { get; set; }

    [ObservableProperty] public partial bool CacheNicoVideo { get; set; }

    [ObservableProperty] public partial bool CacheGeneric { get; set; }

    [ObservableProperty] public partial bool CacheOnly { get; set; }

    // Patching
    [ObservableProperty] public partial bool PatchResonite { get; set; }

    [ConfigBindingIgnore]
    [ObservableProperty] public partial bool PatchVRC { get; set; }

    [ObservableProperty] public partial bool RedirectVRDancing { get; set; }

    // Updates
    [ConfigBindingIgnore]
    [ObservableProperty] public partial bool AutoUpdate { get; set; }

    [ObservableProperty] public partial bool CloseToTray { get; set; }

    [ObservableProperty] public partial bool StartMinimized { get; set; }

    // Blocked URLs
    public ObservableCollection<BlockedUrlEntry> BlockedUrls { get; } = [];

    public ObservableCollection<RedirectUrlEntry> RedirectUrls { get; } = [];

    [ObservableProperty] public partial string BlockRedirect { get; set; } = string.Empty;

    // Status
    private CancellationTokenSource? _statusMessageCts;

    [ConfigBindingIgnore]
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    [ConfigBindingIgnore]
    [ObservableProperty] public partial string StatusMessageColor { get; set; } = string.Empty;

    private void SetStatusMessage(string message, string color, double autoHideSeconds = 3.5)
    {
        _statusMessageCts?.Cancel();
        _statusMessageCts?.TryDispose();
        _statusMessageCts = null;

        StatusMessage = message;
        StatusMessageColor = color;

        if (string.IsNullOrEmpty(message) || autoHideSeconds <= 0) return;

        var cts = new CancellationTokenSource();
        _statusMessageCts = cts;

        _ = Task.Run(async () =>
        {
            await Try.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(autoHideSeconds), cts.Token);
                if (!cts.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!cts.IsCancellationRequested)
                            StatusMessage = string.Empty;
                    });
            }).OnFailure(ex =>
            {
                if (ex is not OperationCanceledException) ex.Throw();
                // Expected when a new status message is set or cleared
                return Unit.TaskValue;
            });
        }, cts.Token);
    }

    [ObservableProperty] public partial bool StartWithSteamVr { get; set; }

    // VR Overlay
    [ObservableProperty] public partial bool VrOverlayEnabled { get; set; }
    [ObservableProperty] public partial bool VrDashboardOverlayEnabled { get; set; }
    [ObservableProperty] public partial bool VrFloatingOverlayEnabled { get; set; }
    [ObservableProperty] public partial bool VrFloatingOverlayOnlyWhenDownloading { get; set; }
    [ObservableProperty] public partial float VrFloatingOverlayDistance { get; set; }
    [ObservableProperty] public partial float VrFloatingOverlayScale { get; set; }

    [ConfigBindingIgnore]
    [ObservableProperty] public partial bool HasChanges { get; set; }

    [ObservableProperty] public partial bool ErrorPopups { get; set; }

    [ObservableProperty] public partial bool ErrorNotifications { get; set; }

    // Pure view state: reveals the advanced setting sections. Not a config value, so it is neither
    // loaded/saved nor tracked as an unsaved change.
    [ConfigBindingIgnore]
    [ObservableProperty] public partial bool ShowAdvanced { get; set; }

    // Language selection
    public static IReadOnlyList<LanguageOption> AvailableLanguageOptions =>
    [
        .. Localizer.Languages.Select(code => new LanguageOption(code, GetLanguageDisplayName(code)))
    ];

    [ConfigBindingIgnore]
    [ObservableProperty] public partial LanguageOption? SelectedLanguageOption { get; set; }

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is null) return;
        Localizer.Language = value.Code;
        ConfigManager.Instance.Config.Language = value.Code;
        ConfigManager.Instance.TrySaveConfig(false);
    }

    private static string GetLanguageDisplayName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).NativeName;
        }
        catch
        {
            return code;
        }
    }

    private void LoadFromConfig()
    {
        _isLoadingConfig = true;
        var config = ConfigManager.Instance.Config;

        ApplyConfigBindings(config);

        // Mappings the generator cannot derive: a combined ComboBox index, renamed fields, and the
        // collections that need view-model wrappers.
        YtdlVp9Mode = config.YtdlpAllowVP9 switch { null => 0, true => 1, false => 2 };
        PreferSabrStreaming = config.SabrPreferStreaming;
        PatchVRC = config.PatchVrChat;
        AutoUpdate = config.AutoUpdateVrcVideoCacher;

        BlockedUrls.Clear();
        foreach (var url in config.BlockedUrls)
            BlockedUrls.Add(new(url));

        RedirectUrls.Clear();
        foreach (var (redirectUrl, redirectTo) in config.RedirectUrls)
            RedirectUrls.Add(new(redirectUrl, redirectTo));

        SelectedLanguageOption = AvailableLanguageOptions.FirstOrDefault(o => o.Code == config.Language)
                                 ?? AvailableLanguageOptions.FirstOrDefault();

        HasChanges = false;
        SetStatusMessage(string.Empty, "#81C784", 0);
        _isLoadingConfig = false;
    }

    private void SetHasChanges()
    {
        if (_isLoadingConfig)
            return;

        HasChanges = true;
        SetStatusMessage(Localizer.Get("SettingsUnsavedChanges"), "#FFB74D", 0);
    }

    private void OnBlockedUrlsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (var oldItem in e.OldItems.OfType<BlockedUrlEntry>())
                oldItem.PropertyChanged -= OnBlockedUrlEntryPropertyChanged;

        if (e.NewItems is not null)
            foreach (var newItem in e.NewItems.OfType<BlockedUrlEntry>())
                newItem.PropertyChanged += OnBlockedUrlEntryPropertyChanged;

        SetHasChanges();
    }

    private void OnBlockedUrlEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlockedUrlEntry.Url))
            SetHasChanges();
    }

    // The generator writes the change handlers for every auto-bound property; these are the ones it
    // keeps out of the generated bindings.
    // ReSharper disable UnusedParameterInPartialMethod
    partial void OnYtdlVp9ModeChanged(int value) => SetHasChanges();
    partial void OnPreferSabrStreamingChanged(bool value) => SetHasChanges();
    partial void OnPatchVRCChanged(bool value) => SetHasChanges();
    partial void OnAutoUpdateChanged(bool value) => SetHasChanges();
    // ReSharper restore UnusedParameterInPartialMethod

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Instance.Config;

        // Restarting the server has to see the old value, so this check runs before the bindings are
        // written back.
        if (config.YtdlpWebServerUrl != WebServerUrl)
        {
            config.YtdlpWebServerUrl = WebServerUrl;
            WebServer.Instance.StartOrRestart();
        }

        CaptureConfigBindings(config);

        // Keep in step with LoadFromConfig.
        config.YtdlpAllowVP9 = YtdlVp9Mode switch { 1 => true, 2 => false, _ => null };
        config.SabrPreferStreaming = PreferSabrStreaming;
        config.PatchVrChat = PatchVRC;
        config.AutoUpdateVrcVideoCacher = AutoUpdate;

        config.BlockedUrls =
        [
            .. BlockedUrls.Select(item => item.Url)
        ];
        config.RedirectUrls = RedirectUrls.ToDictionary(x => x.Url, x => x.RedirectUrl);

        ConfigManager.Instance.TrySaveConfig();
        HasChanges = false;
        SetStatusMessage(Localizer.Get("SettingsSaved"), "#81C784");
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        SetStatusMessage(Localizer.Get("SettingsReset"), "#81C784");
    }

    [RelayCommand]
    private async Task Uninstall()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var confirmed = await ConfirmWindow.ShowAsync(
            desktop.MainWindow!,
            Localizer.Get("Uninstall"),
            Localizer.Get("UninstallConfirm"),
            Localizer.Get("Uninstall"));
        if (!confirmed)
            return;

        Utils.Uninstall.StartDetached();
        desktop.Shutdown();
    }

    [RelayCommand]
    private void AddBlockedUrl()
    {
        BlockedUrls.Add(new("https://"));
    }

    [RelayCommand]
    private void RemoveBlockedUrl(BlockedUrlEntry url)
    {
        BlockedUrls.Remove(url);
    }

    [RelayCommand]
    private void AddRedirectUrl()
    {
        RedirectUrls.Add(new("https://", "https://"));
    }

    [RelayCommand]
    private void RemoveRedirectUrl(RedirectUrlEntry url)
    {
        RedirectUrls.Remove(url);
    }
}