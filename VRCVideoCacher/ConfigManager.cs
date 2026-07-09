using System.Globalization;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher;

public class ConfigManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<ConfigManager>();
    private static readonly string ConfigFilePath;

    static ConfigManager()
    {
        Log.Information("Loading config...");
        ConfigFilePath = Path.Join(Program.DataPath, "Config.json");
        Log.Debug("Using config file path: {ConfigFilePath}", ConfigFilePath);

        ConfigModel? newConfig = null;
        try
        {
            if (File.Exists(ConfigFilePath))
                newConfig = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText(ConfigFilePath));
            if (newConfig != null)
                Config = newConfig;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load config, creating new one...");
        }

        if (Config == null)
        {
            Log.Information("No valid config found, creating new one...");
            Config = new()
            {
                Language = GetSystemLanguage()
            };
            if (!LaunchArgs.HasGui)
                FirstRunConsole();
        }
        else
            Log.Information("Config loaded successfully.");

        if (Config.YtdlpWebServerUrl.EndsWith('/'))
            Config.YtdlpWebServerUrl = Config.YtdlpWebServerUrl.TrimEnd('/');

        Log.Information("Loaded config.");
        TrySaveConfig();
    }

    public static ConfigModel Config { get; }

    // Events for UI
    public static event Action? OnConfigChanged;

    public static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Config changed, saving...");
        File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(Config, Formatting.Indented));
        Log.Information("Config saved.");
        OnConfigChanged?.Invoke();
        CacheManager.TryFlushCache();
    }

    private static bool GetUserConfirmation(string prompt, bool defaultValue)
    {
        var defaultOption = defaultValue ? "Y/n" : "y/N";
        var message = $"{prompt} ({defaultOption}):";
        message = message.TrimStart();
        Log.Information("{UserConfirmationMessage}", message);
        var input = Console.ReadLine();
        return string.IsNullOrEmpty(input) ? defaultValue : input.Equals("y", StringComparison.CurrentCultureIgnoreCase);
    }

    private static void FirstRunConsole()
    {
        Log.Information("It appears this is your first time running VRCVideoCacher. Let's create a basic config file.");

        var autoSetup = GetUserConfirmation("Would you like to use VRCVideoCacher for only fixing YouTube videos?", true);
        if (autoSetup)
            Log.Information("Basic config created. You can modify it later in the Config.json file.");
        else
        {
            Config.CacheYouTube = GetUserConfirmation("Would you like to cache/download Youtube videos?", true);
            if (Config.CacheYouTube)
            {
                var maxResolution = GetUserConfirmation("Would you like to cache/download Youtube videos in 4k?", true);
                Config.CacheYouTubeMaxResolution = maxResolution ? 2160 : 1080;
            }

            var vrDancingPyPyChoice =
                GetUserConfirmation("Would you like to cache/download VRDancing & PyPyDance videos?", true);
            Config.CacheVrDancing = vrDancingPyPyChoice;
            Config.CachePyPyDance = vrDancingPyPyChoice;

            Config.PatchResonite = GetUserConfirmation("Would you like to enable Resonite support?", false);
        }

        if (OperatingSystem.IsWindows() &&
            GetUserConfirmation("Would you like to add VRCVideoCacher to VRCX auto start?", true))
            AutoStartShortcut.CreateShortcut();

        Log.Information(
            "You'll need to install our companion extension to fetch youtube cookies (This will fix YouTube bot errors)");
        Log.Information(
            "Chrome: https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge");
        Log.Information("Firefox: https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter/");
        Log.Information("More info: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
        TrySaveConfig();
    }

    private static string GetSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Localizer.Languages.Contains(culture) ? culture : "en";
    }
}

public class ConfigModel
{
    // Video Cacher
    public bool AutoUpdateVrcVideoCacher = true;

    // Cache Rules
    public string[] BlockedUrls = ["https://na2.vrdancing.club/sampleurl.mp4"];
    public string BlockRedirect = "https://www.youtube.com/watch?v=byv2bKekeWQ";

    // Caching
    public string CachedAssetPath = "";
    public float CacheMaxSizeInGb = 10f;
    public bool CacheOnly = false;
    public bool CachePyPyDance;
    public bool CacheVrDancing;
    public bool CacheYouTube;
    public int CacheYouTubeMaxLength = 120;
    public int CacheYouTubeMaxResolution = 1080;
    public bool CloseToTray = true;
    public bool CookieSetupCompleted = false;
    public bool ErrorPopups = true;

    // Localization
    public string Language = "en";

    // Patching
    public bool PatchResonite;
    public bool PatchVrChat = true;
    public string[] PreCacheUrls = [];
    public bool RedirectVRDancing = false;
    public string ResonitePath = "";
    public bool StartMinimized = false;
    public bool StartWithSteamVr = true;
    public string YtdlpAdditionalArgs = string.Empty;
    public bool YtdlpAutoUpdate = true;
    public string YtdlpDubLanguage = string.Empty;
    public bool YtdlpUseCookies = true;

    // yt-dlp
    public string YtdlpWebServerUrl = "http://localhost:9696";
}