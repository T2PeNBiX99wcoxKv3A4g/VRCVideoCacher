using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public partial class VvcConfigService : Singleton<VvcConfigService>
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

    [PublicAPI] public VvcConfig CurrentConfig2 { get; private set; } = new();
    public static event Action? OnApiConfigChanged;

    [PublicAPI]
    public async Task GetConfig2()
    {
        await Try.Run(async () =>
        {
            var req = await _httpClient.GetAsync("https://vvc.ellyvr.dev/api/v1/config");
            if (req.IsSuccessStatusCode)
            {
                var deserialized = JsonConvert.DeserializeObject<VvcConfig>(await req.Content.ReadAsStringAsync());
                if (deserialized != null)
                {
                    CurrentConfig2 = deserialized;
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