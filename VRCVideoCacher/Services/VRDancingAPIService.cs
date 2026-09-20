using JetBrains.Annotations;
using Newtonsoft.Json;
using VRCVideoCacher.Database;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public partial class VRDancingAPIService : Singleton<VRDancingAPIService>
{
    private const string VRDancingAPIBaseURL = "https://dbapi.vrdancing.club/";

    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new(VRDancingAPIBaseURL),
        DefaultRequestHeaders =
        {
            {
                "User-Agent", $"VRCVideoCacher {Program.Version}"
            }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private async Task<VRDSongInfo?> GetVideoInfo(string code)
    {
        var req = await _httpClient.GetAsync($"/api/v1/public/getsong?code={code}");
        var str = await req.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<VRDSongInfo>(str);
    }

    [PublicAPI]
    public async Task DownloadMetadata2(string code, string videoId)
    {
        await Try.Run(async () =>
        {
            var vrdData = await GetVideoInfo(code);
            if (vrdData == null)
                return;

            await ThumbnailManager.TrySaveThumbnail(videoId, vrdData.ThumbnailURL);
            DatabaseManager.AddVideoInfoCache(new()
            {
                Id = videoId,
                Title = vrdData.Song,
                Author = vrdData.Artist,
                Type = UrlType.VRDancing
            });
        }).OnFailure((ex) =>
        {
            Log.Error("Failed to download video metadata: {Ex}", ex.ToString());
            return Task.FromResult(Unit.Value);
        });
    }
}

public class VRDSongInfo
{
    public string Artist = string.Empty;
    public string Song = string.Empty;
    public string Instructor = string.Empty;
    public string ThumbnailURL = string.Empty;
    public string Hash = string.Empty;
}