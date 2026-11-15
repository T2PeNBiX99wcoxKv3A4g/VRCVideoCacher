using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

[Singleton]
public partial class VvcConfigService
{
    private readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", $"VRCVideoCacher v{Program.Version}"
            }
        }
    };

    [PublicAPI] public VvcConfig CurrentConfig { get; private set; } = new();
    public static event Action? OnApiConfigChanged;

    [PublicAPI]
    public async Task GetConfig()
    {
        await Try.Run(async () =>
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
        }).OnFailure(ex =>
        {
            Log.Warning(ex, "Failed to get config from Video Cacher API.");
            return Unit.TaskValue;
        });
    }
}

[PublicAPI]
public class VvcConfig
{
    [JsonPropertyName("motd")] public string Motd { get; set; } = string.Empty;

    [JsonPropertyName("retryCount")] public int RetryCount { get; set; } = 7;
}