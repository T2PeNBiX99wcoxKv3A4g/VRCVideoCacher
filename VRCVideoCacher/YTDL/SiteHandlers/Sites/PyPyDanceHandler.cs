using System.Web;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class PyPyDanceHandler : Handler<PyPyDanceHandler>, ISiteHandler
{
    private static readonly string[] Prefixes = ["http://api.pypy.dance/video", "https://api.pypy.dance/video"];

    public override bool CanHandle(Uri uri) => Prefixes.Any(p => uri.ToString().StartsWith(p));

    public override async Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var result = await HttpUtil.HttpClient.SendAsync(request);
            var videoUrl = result.RequestMessage?.RequestUri?.ToString();
            if (string.IsNullOrEmpty(videoUrl))
            {
                Log.Error("Failed to get video ID from PypyDance URL: {Url} Response: {Response} - {Data}", url,
                    result.StatusCode, await result.Content.ReadAsStringAsync());
                return null;
            }

            var finalUri = new Uri(videoUrl);
            var fileName = Path.GetFileName(finalUri.LocalPath);
            var videoId = !fileName.Contains('.') ? fileName : fileName.Split('.')[0];

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (int.TryParse(query.Get("id"), out var idInt))
                _ = Task.Run(async () => await PyPyDanceApiService.DownloadMetadata(idInt, videoId));
            else
                Log.Warning("Failed to parse numeric ID from PypyDance URL query for metadata: {Url}", url);

            return new()
            {
                VideoUrl = videoUrl,
                VideoId = videoId,
                UrlType = UrlType.PyPyDance,
                DownloadFormat = DownloadFormat.MP4
            };
        }
        catch
        {
            Log.Error("Failed to get video ID from PypyDance URL: {Url}", url);
            return null;
        }
    }
}