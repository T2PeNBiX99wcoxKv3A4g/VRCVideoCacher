using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using JetBrains.Annotations;
using Swan.Logging;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Services;
using VRCVideoCacher.Services.Nico;
using VRCVideoCacher.Services.Sabr;
using VRCVideoCacher.Utils;
using ILogger = Serilog.ILogger;

namespace VRCVideoCacher.API;

[Singleton]
public partial class WebServer : ILog
{
    private EmbedIO.WebServer? _server;

    ILogger ILog.Log => Log;

    [PublicAPI]
    public void StartOrRestart()
    {
        _server?.TryDispose();

        var indexPath = Path.Join(CacheManager.Instance.CachePath, "index.html");
        if (!File.Exists(indexPath))
            File.WriteAllText(indexPath, "VRCVideoCacher");

        Directory.CreateDirectory(SabrRestreamService.HlsRootPath);
        Directory.CreateDirectory(NicoRestreamService.HlsRootPath);

        _server = CreateWebServer(ConfigManager.Instance.Config.YtdlpWebServerUrl);
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
            // NicoNico HLS sessions and temp video streaming.
            .WithModule(new NicoHlsModule("/nico"))
            .WithStaticFolder("/nico", NicoRestreamService.HlsRootPath, false, m => m
                .WithContentCaching(false))
            // SABR HLS sessions. The module runs first and falls through to the static file module:
            // it builds the requested segment on demand (fetching or seeking as needed) so the file
            // exists by the time the static module sends it. It is also the session's only liveness
            // signal — VRChat asks for the URL once and then pulls media directly, so without these
            // requests the idle reaper would tear a playing session down.
            // Not content-cached: segments appear as the fetch progresses.
            .WithModule(new SabrHlsModule("/hls"))
            .WithStaticFolder("/hls", SabrRestreamService.HlsRootPath, false, m => m
                .WithContentCaching(false))
            .WithStaticFolder("/", CacheManager.Instance.CachePath, true, m => m
                .WithContentCaching(true));

        // Listen for state changes.
        server.StateChanged += (_, e) => $"WebServer State: {e.NewState}".Info();
        server.OnUnhandledException += OnUnhandledException;
        server.OnHttpException += OnHttpException;
        return server;
    }

    private Task OnHttpException(IHttpContext context, IHttpException httpException)
    {
        // A missing cache entry or a failed stream segment is an ordinary 4xx for the player, and the
        // access log already records the request. Only a server-side fault needs a warning.
        if (httpException.StatusCode >= 500)
            Log.Warning(httpException as Exception, "HTTP {StatusCode} for {Method} {Path}",
                httpException.StatusCode, context.Request.HttpMethod, context.RequestedPath);
        else
            Log.Debug("HTTP {StatusCode} for {Method} {Path}", httpException.StatusCode,
                context.Request.HttpMethod, context.RequestedPath);

        return Task.CompletedTask;
    }

    private Task OnUnhandledException(IHttpContext context, Exception exception)
    {
        Log.Error(exception, "Unhandled exception for {Method} {Path}", context.Request.HttpMethod,
            context.RequestedPath);
        return Task.CompletedTask;
    }
}