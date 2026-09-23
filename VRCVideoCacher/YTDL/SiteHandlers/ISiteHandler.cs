using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers;

public interface ISiteHandler
{
    // RewriteUrl runs before CanHandle for resolver type handlers.
    // Direct handlers just implement CanHandle + GetVideoInfo

    bool CanHandle(Uri uri);
    Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro);
    List<string> GetYtdlpArguments(Uri uri, bool avPro);
    Task<string> RewriteUrl(string url, Uri uri);
}