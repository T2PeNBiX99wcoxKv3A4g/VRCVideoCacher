using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.API;

// ReSharper disable MemberCanBeMadeStatic.Global

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

public partial class SettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;

    public SettingsViewModel()
    {
        BlockedUrls.CollectionChanged += OnBlockedUrlsCollectionChanged;
        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();
    }

    // YouTube SABR Options
    [ObservableProperty] public partial bool SabrFilterDrcAudio { get; set; }

    [ObservableProperty] public partial bool SabrFilterSuperResolution { get; set; }

    [ObservableProperty] public partial bool SabrFilterVoiceBoostedAudio { get; set; }

    // Server Settings
    [ObservableProperty] public partial string WebServerUrl { get; set; } = string.Empty;

    // Download Settings
    [ObservableProperty] public partial bool YtdlUseCookies { get; set; }

    [ObservableProperty] public partial bool YtdlAutoUpdate { get; set; }

    [ObservableProperty] public partial string YtdlAdditionalArgs { get; set; } = string.Empty;

    [ObservableProperty] public partial string YtdlDubLanguage { get; set; } = string.Empty;

    // Cache Settings
    [ObservableProperty] public partial string CachedAssetPath { get; set; } = string.Empty;

    [ObservableProperty] public partial bool CacheYouTube { get; set; }

    [ObservableProperty] public partial int CacheYouTubeMaxResolution { get; set; }

    // Resolution options for the dropdown
    public int[] ResolutionOptions { get; } = [720, 1080, 1440, 2160];

    [ObservableProperty] public partial int CacheYouTubeMaxLength { get; set; }

    [ObservableProperty] public partial float CacheMaxSizeInGb { get; set; }

    [ObservableProperty] public partial bool CachePyPyDance { get; set; }

    [ObservableProperty] public partial bool CacheVRDancing { get; set; }

    [ObservableProperty] public partial bool CacheGeneric { get; set; }

    [ObservableProperty] public partial bool CacheOnly { get; set; }

    // Patching
    [ObservableProperty] public partial bool PatchResonite { get; set; }

    [ObservableProperty] public partial bool PatchVRC { get; set; }

    [ObservableProperty] public partial bool RedirectVRDancing { get; set; }

    // Updates
    [ObservableProperty] public partial bool AutoUpdate { get; set; }

    [ObservableProperty] public partial bool CloseToTray { get; set; }

    [ObservableProperty] public partial bool StartMinimized { get; set; }

    // Blocked URLs
    public ObservableCollection<BlockedUrlEntry> BlockedUrls { get; } = [];

    public ObservableCollection<RedirectUrlEntry> RedirectUrls { get; } = [];

    [ObservableProperty] public partial string BlockRedirect { get; set; } = string.Empty;

    // Status
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty] public partial string StatusMessageColor { get; set; } = string.Empty;

    [ObservableProperty] public partial bool StartWithSteamVr { get; set; }

    [ObservableProperty] public partial bool HasChanges { get; set; }

    [ObservableProperty] public partial bool ErrorPopups { get; set; }

    // Language selection
    public static IReadOnlyList<LanguageOption> AvailableLanguageOptions =>
    [
        .. Localizer.Languages.Select(code => new LanguageOption(code, GetLanguageDisplayName(code)))
    ];

    [ObservableProperty] public partial LanguageOption? SelectedLanguageOption { get; set; }

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is null) return;
        Localizer.Language = value.Code;
        ConfigManager.Config.Language = value.Code;
        ConfigManager.TrySaveConfig();
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
        var config = ConfigManager.Config;

        WebServerUrl = config.YtdlpWebServerUrl;
        YtdlUseCookies = config.YtdlpUseCookies;
        YtdlAutoUpdate = config.YtdlpAutoUpdate;
        YtdlAdditionalArgs = config.YtdlpAdditionalArgs;
        YtdlDubLanguage = config.YtdlpDubLanguage;
        SabrFilterDrcAudio = config.SabrFilterDrcAudio;
        SabrFilterSuperResolution = config.SabrFilterSuperResolution;
        SabrFilterVoiceBoostedAudio = config.SabrFilterVoiceBoostedAudio;
        CachedAssetPath = config.CachedAssetPath;
        CacheYouTube = config.CacheYouTube;
        CacheYouTubeMaxResolution = config.CacheYouTubeMaxResolution;
        CacheYouTubeMaxLength = config.CacheYouTubeMaxLength;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        CachePyPyDance = config.CachePyPyDance;
        CacheVRDancing = config.CacheVrDancing;
        CacheGeneric = config.CacheGeneric;
        CacheOnly = config.CacheOnly;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVrChat;
        AutoUpdate = config.AutoUpdateVrcVideoCacher;
        CloseToTray = config.CloseToTray;
        StartMinimized = config.StartMinimized;
        StartWithSteamVr = config.StartWithSteamVr;
        ErrorPopups = config.ErrorPopups;
        RedirectVRDancing = config.RedirectVRDancing;
        BlockedUrls.Clear();
        foreach (var url in config.BlockedUrls)
            BlockedUrls.Add(new(url));
        BlockRedirect = config.BlockRedirect;
        RedirectUrls.Clear();
        foreach (var (redirectUrl, redirectTo) in config.RedirectUrls)
            RedirectUrls.Add(new(redirectUrl, redirectTo));

        SelectedLanguageOption = AvailableLanguageOptions.FirstOrDefault(o => o.Code == config.Language)
                                 ?? AvailableLanguageOptions.FirstOrDefault();

        HasChanges = false;
        StatusMessage = string.Empty;
        StatusMessageColor = "#81C784";
        _isLoadingConfig = false;
    }

    private void SetHasChanges()
    {
        if (_isLoadingConfig)
            return;

        HasChanges = true;
        StatusMessage = Localizer.Get("SettingsUnsavedChanges");
        StatusMessageColor = "#FFB74D";
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

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Config;

        if (config.YtdlpWebServerUrl != WebServerUrl)
        {
            config.YtdlpWebServerUrl = WebServerUrl;
            WebServer.Init();
        }

        config.YtdlpUseCookies = YtdlUseCookies;
        config.YtdlpAutoUpdate = YtdlAutoUpdate;
        config.YtdlpAdditionalArgs = YtdlAdditionalArgs;
        config.YtdlpDubLanguage = YtdlDubLanguage;
        config.SabrFilterDrcAudio = SabrFilterDrcAudio;
        config.SabrFilterSuperResolution = SabrFilterSuperResolution;
        config.SabrFilterVoiceBoostedAudio = SabrFilterVoiceBoostedAudio;
        config.CachedAssetPath = CachedAssetPath;
        config.CacheYouTube = CacheYouTube;
        config.CacheYouTubeMaxResolution = CacheYouTubeMaxResolution;
        config.CacheYouTubeMaxLength = CacheYouTubeMaxLength;
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.CachePyPyDance = CachePyPyDance;
        config.CacheVrDancing = CacheVRDancing;
        config.CacheGeneric = CacheGeneric;
        config.CacheOnly = CacheOnly;
        config.PatchResonite = PatchResonite;
        config.PatchVrChat = PatchVRC;
        config.AutoUpdateVrcVideoCacher = AutoUpdate;
        config.CloseToTray = CloseToTray;
        config.StartMinimized = StartMinimized;
        config.StartWithSteamVr = StartWithSteamVr;
        config.ErrorPopups = ErrorPopups;
        config.BlockedUrls =
        [
            .. BlockedUrls.Select(item => item.Url)
        ];
        config.BlockRedirect = BlockRedirect;
        config.RedirectUrls = RedirectUrls.ToDictionary(x => x.Url, x => x.RedirectUrl);
        config.RedirectVRDancing = RedirectVRDancing;
        ConfigManager.TrySaveConfig();
        HasChanges = false;
        StatusMessage = Localizer.Get("SettingsSaved");
        StatusMessageColor = "#81C784";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = Localizer.Get("SettingsReset");
        StatusMessageColor = "#81C784";
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

    // ReSharper disable UnusedParameterInPartialMethod
    partial void OnWebServerUrlChanged(string value) => SetHasChanges();
    partial void OnYtdlUseCookiesChanged(bool value) => SetHasChanges();
    partial void OnYtdlAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnYtdlAdditionalArgsChanged(string value) => SetHasChanges();
    partial void OnYtdlDubLanguageChanged(string value) => SetHasChanges();
    partial void OnSabrFilterDrcAudioChanged(bool value) => SetHasChanges();
    partial void OnSabrFilterSuperResolutionChanged(bool value) => SetHasChanges();
    partial void OnSabrFilterVoiceBoostedAudioChanged(bool value) => SetHasChanges();
    partial void OnCachedAssetPathChanged(string value) => SetHasChanges();
    partial void OnCacheYouTubeChanged(bool value) => SetHasChanges();
    partial void OnCacheYouTubeMaxResolutionChanged(int value) => SetHasChanges();
    partial void OnCacheYouTubeMaxLengthChanged(int value) => SetHasChanges();
    partial void OnCacheMaxSizeInGbChanged(float value) => SetHasChanges();
    partial void OnCachePyPyDanceChanged(bool value) => SetHasChanges();
    partial void OnCacheVRDancingChanged(bool value) => SetHasChanges();
    partial void OnCacheOnlyChanged(bool value) => SetHasChanges();
    partial void OnPatchResoniteChanged(bool value) => SetHasChanges();
    partial void OnPatchVRCChanged(bool value) => SetHasChanges();
    partial void OnAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnCloseToTrayChanged(bool value) => SetHasChanges();
    partial void OnStartMinimizedChanged(bool value) => SetHasChanges();
    partial void OnStartWithSteamVrChanged(bool value) => SetHasChanges();
    partial void OnBlockRedirectChanged(string value) => SetHasChanges();

    partial void OnErrorPopupsChanged(bool value) => SetHasChanges();
    // ReSharper restore UnusedParameterInPartialMethod
}