using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class GenericHandler : Handler<GenericHandler>
{
    public override bool CanHandle(Uri uri) => true; // always matches, must be last in registry

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var videoId = VideoId.HashUrl(url);
        Log.Information("No specific handler found for URL, using generic handler: {Url}", url);
        return Task.FromResult<VideoInfo?>(new()
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });
    }
}