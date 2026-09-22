using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using VRCVideoCacher.Database;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

internal class PyPyDanceBundle
{
    [JsonPropertyName("songs")] public List<PyPyDanceSong>? Songs { get; set; }
}

public class PyPyDanceSong
{
    [JsonPropertyName("i")] public int? Id { get; set; }

    [JsonPropertyName("n")] public string? Name { get; set; }

    [JsonPropertyName("s")] public int? StartTime { get; set; }

    [JsonPropertyName("e")] public int? EndTime { get; set; }
}

[JsonSerializable(typeof(PyPyDanceBundle))]
internal partial class PyPyDanceBundleContext : JsonSerializerContext
{
}

public partial class PyPyDanceApiService : Singleton<PyPyDanceApiService>
{
    private const string PyPyDanceApiUrl = "https://api.pypy.dance/bundle";
    private DateTime _lastFetch = DateTime.MinValue;
    private List<PyPyDanceSong> _songs = [];

    private readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", $"VRCVideoCacher {Program.Version}"
            }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private async Task<PyPyDanceSong?> GetVideoInfo(int? videoId)
    {
        if (videoId is 0 or null) return null;

        return await Try.Run(async () =>
        {
            if ((DateTime.Now - _lastFetch).TotalMinutes > 60)
                await FetchBundle();

            return _songs.Find(song => song.Id == videoId);
        }).GetOrElse(_ => Task.FromResult<PyPyDanceSong?>(null));
    }

    private async Task FetchBundle()
    {
        _lastFetch = DateTime.Now;
        var req = await _httpClient.GetStringAsync(PyPyDanceApiUrl);
        var bundle = JsonSerializer.Deserialize(req, PyPyDanceBundleContext.Default.PyPyDanceBundle);
        if (bundle?.Songs != null)
            _songs = bundle.Songs;
    }

    [PublicAPI]
    public async Task DownloadMetadata2(int idInt, string videoId)
    {
        await Try.Run(async () =>
        {
            var thumbnailUrl = $"https://api.pypy.dance/thumb?id={idInt}";
            await ThumbnailManager.TrySaveThumbnail(videoId, thumbnailUrl);

            var songInfo = await GetVideoInfo(idInt);
            int? duration = null;
            if (songInfo?.EndTime != null)
                duration = songInfo.EndTime;
            if (songInfo?.StartTime != null && duration != null)
                duration -= songInfo.StartTime;

            await DatabaseManager.AddVideoInfoCacheAsync(new()
            {
                Id = videoId,
                Title = songInfo?.Name,
                Author = null,
                Duration = duration,
                Type = UrlType.PyPyDance
            });
        }).OnFailure(ex =>
        {
            Log.Error("Failed to download video metadata: {Ex}", ex.ToString());
            return Unit.TaskValue;
        });
    }
}