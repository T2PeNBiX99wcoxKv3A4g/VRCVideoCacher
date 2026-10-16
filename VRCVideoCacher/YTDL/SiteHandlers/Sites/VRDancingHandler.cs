using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class VRDancingHandler : Handler<VRDancingHandler>
{
    private static readonly string[] Prefixes =
        ["https://na2.vrdancing.club", "https://eu2.vrdancing.club", "https://mpegts-beta.vrdancing.club"];

    public override bool CanHandle(Uri uri) => Prefixes.Any(p => uri.ToString().StartsWith(p));

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var code = url.TrimEnd('/').Split('/').Last();
        var videoId = VideoId.HashUrl(url);
        if (url.Contains("weekend"))
        {
            // Strip weekend session custom urls from caching. 
            return Task.FromResult<VideoInfo?>(new VideoInfo
            {
                VideoUrl = url,
                VideoId = videoId,
                UrlType = UrlType.Other,
                DownloadFormat = DownloadFormat.MP4
            });
            
        }
        

        // _ = Task.Run(async () =>
        // {
        //     await VRDancingAPIService.DownloadMetadata(code, videoId);
        // });

        return Task.FromResult<VideoInfo?>(new()
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.VRDancing,
            DownloadFormat = DownloadFormat.MP4
        });
    }
}