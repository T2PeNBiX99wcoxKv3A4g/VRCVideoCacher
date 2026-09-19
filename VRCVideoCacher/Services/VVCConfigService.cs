using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public class VvcConfigService : Singleton<VvcConfigService>
{
    private readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", $"VRCVideoCacher v{Program.Version}" } }
    };

    public VvcConfig CurrentConfig { get; private set; } = new();
    public event Action? OnApiConfigChanged;

    public async Task GetConfig()
    {
        try
        {
            var req = await _httpClient.GetAsync("https://vvc.ellyvr.dev/api/v1/config");
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
            Log.Warning(ex, "Failed to get config from Video Cacher API.");
        }
    }
}

[PublicAPI]
public class VvcConfig
{
    [JsonPropertyName("motd")] public string Motd { get; set; } = string.Empty;

    [JsonPropertyName("retryCount")] public int RetryCount { get; set; } = 7;
}