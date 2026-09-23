using JetBrains.Annotations;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers;

[PublicAPI]
public abstract class Handler<T> : ISiteHandler where T : Handler<T>, new()
{
    protected readonly ILogger Log = Program.Logger.ForContext<T>();
    public abstract bool CanHandle(Uri uri);
    public abstract Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro);
    public virtual List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];
    public virtual Task<string> RewriteUrl(string url, Uri uri) => Task.FromResult(url);
}