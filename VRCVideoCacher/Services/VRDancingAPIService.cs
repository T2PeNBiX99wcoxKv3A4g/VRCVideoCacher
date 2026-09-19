using Newtonsoft.Json;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public class VRDancingAPIService : Singleton<VRDancingAPIService>
{
    private const string VRDancingAPIBaseURL = "https://dbapi.vrdancing.club/";
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new(VRDancingAPIBaseURL),
        DefaultRequestHeaders = { { "User-Agent", $"VRCVideoCacher {Program.Version}" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private async Task<VRDSongInfo?> GetVideoInfo(string code)
    {
        var req = await _httpClient.GetAsync($"/api/v1/public/getsong?code={code}");
        var str = await req.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<VRDSongInfo>(str);
    }

    public async Task DownloadMetadata(string code, string videoId)
    {
        try
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
        }
        catch (Exception ex)
        {
            Log.Error("Failed to download video metadata: {Ex}", ex.ToString());
        }
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