using System.Text;
using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using JetBrains.Annotations;
using Swan.Logging;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using ILogger = Serilog.ILogger;

namespace VRCVideoCacher.API;

public partial class WebServer : Singleton<WebServer>, ILog
{
    private EmbedIO.WebServer? _server;

    ILogger ILog.Log => Log;

    [PublicAPI]
    public void StartOrRestart2()
    {
        _server?.Dispose();

        var indexPath = Path.Join(CacheManager.CachePath, "index.html");
        if (!File.Exists(indexPath))
            File.WriteAllText(indexPath, "VRCVideoCacher");

        Directory.CreateDirectory(SabrRestreamService.HlsRootPath);

        _server = CreateWebServer(ConfigManager.Config.YtdlpWebServerUrl);
        _server.RunAsync();
    }

    private EmbedIO.WebServer CreateWebServer(string url)
    {
        Try.Run(Logger.UnregisterLogger<ConsoleLogger>);
        Try.Run(Logger.UnregisterLogger<WebServerLogger>);

        Logger.RegisterLogger<WebServerLogger>();

        var urls = new List<string>
        {
            "http://localhost:9696",
            "http://127.0.0.1:9696"
        };
        if (!urls.Contains(url))
            urls.Add(url);

        var server = new EmbedIO.WebServer(o => o
                .WithUrlPrefixes(urls)
                .WithMode(HttpListenerMode.EmbedIO))
            // First, we will configure our web server by adding Modules.
            .WithWebApi("/api", m => m
                .WithController<ApiController>())
            .WithModule(new NicoRestreamModule("/nico"))
            // SABR HLS sessions. The module runs first and falls through to the static file module:
            // it builds the requested segment on demand (fetching or seeking as needed) so the file
            // exists by the time the static module sends it. It is also the session's only liveness
            // signal — VRChat asks for the URL once and then pulls media directly, so without these
            // requests the idle reaper would tear a playing session down.
            // Not content-cached: segments appear as the fetch progresses.
            .WithModule(new SabrHlsModule("/hls"))
            .WithStaticFolder("/hls", SabrRestreamService.HlsRootPath, false, m => m
                .WithContentCaching(false))
            .WithStaticFolder("/", CacheManager.CachePath, true, m => m
                .WithContentCaching(true));

        // Listen for state changes.
        server.StateChanged += (_, e) => $"WebServer State: {e.NewState}".Info();
        server.OnUnhandledException += OnUnhandledException;
        server.OnHttpException += OnHttpException;
        return server;
    }

    private Task OnHttpException(IHttpContext context, IHttpException httpException)
    {
        Log.Information("OnHttpException Error Occured: {ErrorMessage}", httpException.Message!);
        return Task.CompletedTask;
    }

    private Task OnUnhandledException(IHttpContext context, Exception exception)
    {
        Log.Information(exception, "OnUnhandledException Error Occured");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Materialises the requested SABR HLS file, then lets the static file module actually serve it
/// (<see cref="IsFinalHandler"/> is false, so routing continues).
/// </summary>
internal sealed class SabrHlsModule(string baseRoute) : WebModuleBase(baseRoute)
{
    public override bool IsFinalHandler => false;

    protected override async Task OnRequestAsync(IHttpContext context)
    {
        // A LIVE playlist is answered here rather than falling through to the static file module: that
        // module would attach an ETag and honour If-None-Match, and a playlist that changes every couple
        // of seconds must never be answered 304. Everything else (segments, init, VOD playlists) falls
        // through as before.
        if (await SabrRestreamService.TryGetLivePlaylistAsync(context.RequestedPath) is { } playlist)
        {
            context.Response.ContentType = "application/vnd.apple.mpegurl";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            await context.SendStringAsync(playlist, "application/vnd.apple.mpegurl", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        await SabrRestreamService.EnsureAsync(context.RequestedPath);
    }
}

/// <summary>
/// Handles NicoVideo HLS restream proxy and temporary video streaming.
/// </summary>
internal sealed class NicoRestreamModule(string baseRoute) : WebModuleBase(baseRoute)
{
    public override bool IsFinalHandler => true;

    protected override async Task OnRequestAsync(IHttpContext context)
    {
        var path = context.RequestedPath.TrimStart('/');
        if (path.StartsWith("temp/", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = path[5..];
            await NicoRestreamService.HandleTempVideoAsync(context, fileName);
            return;
        }

        var parts = path.Split('/');
        if (parts.Length >= 2)
        {
            var videoId = parts[0];
            var action = parts[1];

            if (action.Equals("master.m3u8", StringComparison.OrdinalIgnoreCase))
            {
                await NicoRestreamService.HandleMasterPlaylistAsync(context, videoId);
                return;
            }

            if (action.Equals("audio.m3u8", StringComparison.OrdinalIgnoreCase))
            {
                await NicoRestreamService.HandleAudioPlaylistAsync(context, videoId);
                return;
            }

            if (action.Equals("video.m3u8", StringComparison.OrdinalIgnoreCase))
            {
                await NicoRestreamService.HandleVideoPlaylistAsync(context, videoId);
                return;
            }

            if (action.Equals("proxy", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
            {
                var segmentName = parts[2];
                await NicoRestreamService.HandleProxyAsync(context, videoId, segmentName);
                return;
            }
        }

        context.Response.StatusCode = 404;
        await context.SendStringAsync("Not Found", "text/plain", Encoding.UTF8);
        context.SetHandled();
    }
}