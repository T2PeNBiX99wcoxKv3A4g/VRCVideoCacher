using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using EmbedIO;
using JetBrains.Annotations;
using Serilog;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

public class NicoSession
{
    public string VideoId { get; set; } = "";
    public string? MasterUrl { get; set; }
    public Dictionary<string, string> Cookies { get; set; } = new();
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    public TempDir? TempDir { get; set; }
    public string? TempFilePath { get; set; }
}

public static partial class NicoRestreamService
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(NicoRestreamService));

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly ConcurrentDictionary<string, NicoSession> Sessions = new();

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseCookies = false,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    [GeneratedRegex(@"URI=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex UriAttributeRegex();

    static NicoRestreamService()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAll();
        Task.Run(ReaperLoop);
    }

    private static void CleanupAll()
    {
        foreach (var session in Sessions.Values)
        {
            Try.Run(() => session.TempDir?.Dispose());
        }
        Sessions.Clear();
    }

    private static async Task ReaperLoop()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            var now = DateTime.UtcNow;
            foreach (var (id, session) in Sessions)
            {
                if (now - session.LastAccess > TimeSpan.FromMinutes(15))
                {
                    if (Sessions.TryRemove(id, out var removed))
                    {
                        Try.Run(() => removed.TempDir?.Dispose());
                    }
                }
            }
        }
    }

    [PublicAPI]
    public static async Task<string?> GetRestreamUrlAsync(VideoInfo videoInfo)
    {
        var videoId = videoInfo.VideoId;
        var session = await EnsureSessionAsync(videoId);
        if (session == null || string.IsNullOrEmpty(session.MasterUrl))
            return null;

        var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
        return $"{baseUrl}/nico/{videoId}/master.m3u8";
    }

    [PublicAPI]
    public static async Task<string?> DownloadTempVideoAsync(VideoInfo videoInfo, TimeSpan timeout)
    {
        var videoId = videoInfo.VideoId;
        var session = Sessions.GetOrAdd(videoId, id => new NicoSession { VideoId = id });
        session.LastAccess = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(session.TempFilePath) && File.Exists(session.TempFilePath))
        {
            var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
            return $"{baseUrl}/nico/temp/{videoId}.mp4";
        }

        var tempDir = new TempDir();
        var tempDownloadPath = Path.Join(tempDir.FullName, $"{videoId}.mp4");

        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = YtdlManager.YtdlPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Arguments = $"-q -o \"{tempDownloadPath}\" --remux-video mp4 \"{videoInfo.VideoUrl}\""
        };

        process.Start();
        var error = await ChildProcessTracker.Tracking(process, async () =>
        {
            await process.WaitForExitAsync();
            return (await process.StandardError.ReadToEndAsync()).Trim();
        });

        if (process.ExitCode != 0 || !File.Exists(tempDownloadPath))
        {
            tempDir.Dispose();
            Log.Warning("Failed to download temporary NicoVideo: {ExitCode} {VideoId} {Error}", process.ExitCode, videoId, error);
            return null;
        }

        session.TempDir?.Dispose();
        session.TempDir = tempDir;
        session.TempFilePath = tempDownloadPath;

        var url = $"{ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/')}/nico/temp/{videoId}.mp4";
        return url;
    }

    private static async Task<NicoSession?> EnsureSessionAsync(string videoId)
    {
        if (Sessions.TryGetValue(videoId, out var existing) &&
            DateTime.UtcNow < existing.ExpiresAt &&
            !string.IsNullOrEmpty(existing.MasterUrl))
        {
            existing.LastAccess = DateTime.UtcNow;
            return existing;
        }

        var res = await NicoVideoApiService.FetchVideoResult(videoId);
        if (res == null || string.IsNullOrEmpty(res.StreamUrl))
            return null;

        var session = Sessions.GetOrAdd(videoId, id => new NicoSession { VideoId = id });
        session.MasterUrl = res.StreamUrl;
        session.Cookies = res.Cookies;
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
        session.LastAccess = DateTime.UtcNow;
        return session;
    }

    public static async Task HandleMasterPlaylistAsync(IHttpContext context, string videoId)
    {
        var session = await EnsureSessionAsync(videoId);
        if (session == null || string.IsNullOrEmpty(session.MasterUrl))
        {
            context.Response.StatusCode = 404;
            await context.SendStringAsync("Master playlist not found", "text/plain", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        session.LastAccess = DateTime.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Get, session.MasterUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");

        var cookieHeader = string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Nico master playlist fetch failed with code {Code} for {VideoId}", response.StatusCode, videoId);
            context.Response.StatusCode = (int)response.StatusCode;
            await context.SendStringAsync("Failed to fetch master playlist", "text/plain", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        var text = await response.Content.ReadAsStringAsync();
        var rewritten = RewritePlaylist(text, session.MasterUrl, videoId);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/vnd.apple.mpegurl";
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";

        await context.SendStringAsync(rewritten, "application/vnd.apple.mpegurl", Encoding.UTF8);
        context.SetHandled();
    }

    public static async Task HandleProxyAsync(IHttpContext context, string videoId)
    {
        var session = await EnsureSessionAsync(videoId);
        if (session == null)
        {
            context.Response.StatusCode = 404;
            await context.SendStringAsync("Session not found", "text/plain", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        session.LastAccess = DateTime.UtcNow;

        var targetUrl = context.Request.QueryString["url"];
        if (string.IsNullOrEmpty(targetUrl))
        {
            var query = context.Request.Url.Query;
            if (query.StartsWith("?url=", StringComparison.OrdinalIgnoreCase))
                targetUrl = Uri.UnescapeDataString(query[5..]);
        }

        if (string.IsNullOrEmpty(targetUrl))
        {
            context.Response.StatusCode = 400;
            await context.SendStringAsync("Missing url parameter", "text/plain", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(targetUrl));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");

        var cookieHeader = string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        if (context.Request.Headers["Range"] is { } range)
            request.Headers.TryAddWithoutValidation("Range", range);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Nico proxy request failed ({Code}) for {Url}", response.StatusCode, targetUrl);
            context.Response.StatusCode = (int)response.StatusCode;
            context.SetHandled();
            return;
        }

        var isPlaylist = targetUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                         response.Content.Headers.ContentType?.MediaType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) == true;

        if (isPlaylist)
        {
            var text = await response.Content.ReadAsStringAsync();
            var rewritten = RewritePlaylist(text, targetUrl, videoId);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/vnd.apple.mpegurl";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";

            await context.SendStringAsync(rewritten, "application/vnd.apple.mpegurl", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        // Media segment
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "video/mp4";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";

        if (response.Content.Headers.ContentLength.HasValue)
            context.Response.ContentLength64 = response.Content.Headers.ContentLength.Value;

        if (response.Content.Headers.TryGetValues("Content-Range", out var cr))
            context.Response.Headers["Content-Range"] = string.Join(", ", cr);

        if (response.Headers.TryGetValues("Accept-Ranges", out var ar))
            context.Response.Headers["Accept-Ranges"] = string.Join(", ", ar);

        using (var resStream = context.OpenResponseStream())
        using (var srcStream = await response.Content.ReadAsStreamAsync())
        {
            await srcStream.CopyToAsync(resStream);
        }

        context.SetHandled();
    }

    public static async Task HandleTempVideoAsync(IHttpContext context, string videoId)
    {
        if (!Sessions.TryGetValue(videoId, out var session) ||
            string.IsNullOrEmpty(session.TempFilePath) ||
            !File.Exists(session.TempFilePath))
        {
            context.Response.StatusCode = 404;
            await context.SendStringAsync("Temp video not found", "text/plain", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        session.LastAccess = DateTime.UtcNow;
        await ServeFileWithRangeAsync(context, session.TempFilePath, "video/mp4");
    }

    private static async Task ServeFileWithRangeAsync(IHttpContext context, string filePath, string contentType)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            context.Response.StatusCode = 404;
            context.SetHandled();
            return;
        }

        var totalLength = fileInfo.Length;
        var rangeHeader = context.Request.Headers["Range"];

        context.Response.Headers["Accept-Ranges"] = "bytes";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.ContentType = contentType;

        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = totalLength;
            using var fs = File.OpenRead(filePath);
            using var os = context.OpenResponseStream();
            await fs.CopyToAsync(os);
            context.SetHandled();
            return;
        }

        var rangeValue = rangeHeader["bytes=".Length..].Trim();
        var parts = rangeValue.Split('-');
        long start = 0;
        long end = totalLength - 1;

        if (!string.IsNullOrEmpty(parts[0]))
            long.TryParse(parts[0], out start);
        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
            long.TryParse(parts[1], out end);

        if (start > end || start >= totalLength)
        {
            context.Response.StatusCode = 416;
            context.Response.Headers["Content-Range"] = $"bytes */{totalLength}";
            context.SetHandled();
            return;
        }

        end = Math.Min(end, totalLength - 1);
        var length = end - start + 1;

        context.Response.StatusCode = 206;
        context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{totalLength}";
        context.Response.ContentLength64 = length;

        using (var fs = File.OpenRead(filePath))
        using (var os = context.OpenResponseStream())
        {
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[64 * 1024];
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await fs.ReadAsync(buffer.AsMemory(0, toRead));
                if (read == 0) break;
                await os.WriteAsync(buffer.AsMemory(0, read));
                remaining -= read;
            }
        }
        context.SetHandled();
    }

    public static string RewritePlaylist(string playlistText, string baseUrl, string videoId)
    {
        var lines = playlistText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var sb = new StringBuilder(playlistText.Length + 512);
        var baseUri = new Uri(baseUrl);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(line);
                continue;
            }

            if (line.StartsWith('#'))
            {
                if (line.Contains("URI=\"", StringComparison.OrdinalIgnoreCase))
                {
                    var replaced = UriAttributeRegex().Replace(line, match =>
                    {
                        var uriValue = match.Groups[1].Value;
                        if (Uri.TryCreate(baseUri, uriValue, out var resolvedUri))
                        {
                            var proxied = $"/nico/{videoId}/proxy?url={Uri.EscapeDataString(resolvedUri.AbsoluteUri)}";
                            return $"URI=\"{proxied}\"";
                        }
                        return match.Value;
                    });
                    sb.AppendLine(replaced);
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
            else
            {
                if (Uri.TryCreate(baseUri, line.Trim(), out var resolvedUri))
                {
                    var proxied = $"/nico/{videoId}/proxy?url={Uri.EscapeDataString(resolvedUri.AbsoluteUri)}";
                    sb.AppendLine(proxied);
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
        }

        return sb.ToString();
    }
}
