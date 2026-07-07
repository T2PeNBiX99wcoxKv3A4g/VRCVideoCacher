using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher.Services;

public class VvcConfigService
{
    private static readonly HttpClient HttpClient;
    public static event Action? OnApiConfigChanged;
    public static ILogger Logger = Log.ForContext<VvcConfigService>();
    public static VvcConfig CurrentConfig { get; private set; } = new();

    static VvcConfigService()
    {
        HttpClient = new();
        HttpClient.DefaultRequestHeaders.Add("User-Agent", $"VRCVideoCacher v{Program.Version}");
    }
    public static async Task GetConfig()
    {
        try
        {
            var req = await HttpClient.GetAsync("https://vvc.ellyvr.dev/api/v1/config");
            if (req.IsSuccessStatusCode)
            {
                var deserialized = JsonConvert.DeserializeObject<VvcConfig>(await req.Content.ReadAsStringAsync());
                if (deserialized != null)
                {
                    CurrentConfig = deserialized;
                    OnApiConfigChanged?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to get config from Video Cacher API.");
        }
        
    }
}

public class VvcConfig
{
    [JsonPropertyName("motd")]
    public string Motd { get; set; } = string.Empty;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 7;
}