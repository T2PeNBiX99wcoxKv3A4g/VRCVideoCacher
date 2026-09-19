using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class YTSHandler : Handler<YTSHandler>, ISiteHandler
{
    public override bool CanHandle(Uri uri) => false; // rewrite only

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);

    public Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!url.StartsWith("https://dmn.moe"))
            return Task.FromResult(url);

        var newUrl = url.Replace("/sr/", "/yt/");
        Log.Information("YTS URL detected, modified to: {URL}", newUrl);
        return Task.FromResult(newUrl);
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];
}