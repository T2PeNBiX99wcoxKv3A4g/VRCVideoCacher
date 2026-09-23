using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
    public string? SelectedVideoPlaylistUrl { get; set; }
    public string? SelectedAudioPlaylistUrl { get; set; }
    public Dictionary<string, string> Cookies { get; set; } = new();
    public ConcurrentDictionary<string, string> UrlMap { get; } = new();
    public ConcurrentDictionary<string, string> ReverseUrlMap { get; } = new();
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    public TempDir? TempDir { get; set; }
    public string? TempFilePath { get; set; }

    public string GetOrAddUrlToken(string upstreamUrl, string extension)
    {
        return ReverseUrlMap.GetOrAdd(upstreamUrl, u =>
        {
            var token = $"{Guid.NewGuid():N}"[..12];
            var key = $"{token}{extension}";
            UrlMap[key] = u;
            return key;
        });
    }
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

    private static bool _isExit;

    [GeneratedRegex(@"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=""([^""]+)"",NAME=""([^""]+)"",([^\n]*)URI=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AudioMediaRegex();

    [GeneratedRegex(@"#EXT-X-STREAM-INF:([^\n]*BANDWIDTH=(\d+)[^\n]*)", RegexOptions.Compiled)]
    private static partial Regex StreamInfRegex();

    [GeneratedRegex(@"AUDIO=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AudioGroupAttrRegex();

    [GeneratedRegex(@"URI=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex GenericUriAttrRegex();

    static NicoRestreamService()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAll();
        Task.Run(ReaperLoop);
    }

    private static void CleanupAll()
    {
        if (Interlocked.Exchange(ref _isExit, true)) return;
        foreach (var session in Sessions.Values)
            Try.Run(() => session.TempDir?.Dispose());
        Sessions.Clear();
    }

    private static async Task ReaperLoop()
    {
        while (!Volatile.Read(ref _isExit))
        {
            if (Volatile.Read(ref _isExit)) break;
            await Task.Delay(TimeSpan.FromMinutes(2));
            var now = DateTime.UtcNow;
            foreach (var (id, session) in Sessions)
                if (now - session.LastAccess > TimeSpan.FromMinutes(15))
                    if (Sessions.TryRemove(id, out var removed))
                        Try.Run(() => removed.TempDir?.Dispose());
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
        var session = Sessions.GetOrAdd(videoId, id => new()
        {
            VideoId = id
        });
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

        var res = await NicoVideoApiService.Instance.FetchVideoResult2(videoId);
        if (res == null || string.IsNullOrEmpty(res.StreamUrl))
            return null;

        var session = Sessions.GetOrAdd(videoId, id => new() { VideoId = id });
        session.MasterUrl = res.StreamUrl;
        session.Cookies = res.Cookies;
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(10);
        session.LastAccess = DateTime.UtcNow;
        return session;
    }

    public static async Task HandleMasterPlaylistAsync(IHttpContext context, string videoId)
    {
        var session = await EnsureSessionAsync(videoId);
        if (session == null || string.IsNullOrEmpty(session.MasterUrl))
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Master playlist not found", "text/plain");
            return;
        }

        session.LastAccess = DateTime.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Get, session.MasterUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.nicovideo.jp/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.nicovideo.jp");

        var cookieHeader = string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Nico master playlist fetch failed with code {Code} for {VideoId}", response.StatusCode, videoId);
            context.Response.StatusCode = (int)response.StatusCode;
            await SendStringWithLengthAsync(context, "Failed to fetch master playlist", "text/plain");
            return;
        }

        var text = await response.Content.ReadAsStringAsync();
        var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
        var rewritten = RecreateMasterPlaylist(text, session.MasterUrl, session, baseUrl);

        context.Response.StatusCode = 200;
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";

        await SendStringWithLengthAsync(context, rewritten, "application/vnd.apple.mpegurl");
    }

    public static async Task HandleAudioPlaylistAsync(IHttpContext context, string videoId)
    {
        var session = await EnsureSessionAsync(videoId);
        if (session == null || string.IsNullOrEmpty(session.SelectedAudioPlaylistUrl))
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Audio playlist not found", "text/plain");
            return;
        }

        session.LastAccess = DateTime.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Get, session.SelectedAudioPlaylistUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.nicovideo.jp/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.nicovideo.jp");

        var cookieHeader = string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Nico audio playlist fetch failed with code {Code} for {VideoId}", response.StatusCode, videoId);
            context.Response.StatusCode = (int)response.StatusCode;
            await SendStringWithLengthAsync(context, "Failed to fetch audio playlist", "text/plain");
            return;
        }

        var text = await response.Content.ReadAsStringAsync();
        var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
        var rewritten = RewriteVariantPlaylist(text, session.SelectedAudioPlaylistUrl, session, baseUrl, isAudio: true);

        context.Response.StatusCode = 200;
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";

        await SendStringWithLengthAsync(context, rewritten, "application/vnd.apple.mpegurl");
    }

    public static async Task HandleVideoPlaylistAsync(IHttpContext context, string videoId)
    {
        var session = await EnsureSessionAsync(videoId);
        if (session == null || string.IsNullOrEmpty(session.SelectedVideoPlaylistUrl))
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Video playlist not found", "text/plain");
            return;
        }

        session.LastAccess = DateTime.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Get, session.SelectedVideoPlaylistUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.nicovideo.jp/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.nicovideo.jp");

        var cookieHeader = string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Nico video playlist fetch failed with code {Code} for {VideoId}", response.StatusCode, videoId);
            context.Response.StatusCode = (int)response.StatusCode;
            await SendStringWithLengthAsync(context, "Failed to fetch video playlist", "text/plain");
            return;
        }

        var text = await response.Content.ReadAsStringAsync();
        var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
        var rewritten = RewriteVariantPlaylist(text, session.SelectedVideoPlaylistUrl, session, baseUrl, isAudio: false);

        context.Response.StatusCode = 200;
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";

        await SendStringWithLengthAsync(context, rewritten, "application/vnd.apple.mpegurl");
    }

    public static async Task HandleProxyAsync(IHttpContext context, string videoId, string fileName)
    {
        var session = await EnsureSessionAsync(videoId);
        if (session == null)
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Session not found", "text/plain");
            return;
        }

        session.LastAccess = DateTime.UtcNow;

        if (string.IsNullOrEmpty(fileName) || !session.UrlMap.TryGetValue(fileName, out var targetUrl))
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Segment not found", "text/plain");
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(targetUrl));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.nicovideo.jp/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.nicovideo.jp");

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

        context.Response.StatusCode = (int)response.StatusCode;

        string contentType;
        if (fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
            contentType = "application/octet-stream";
        else if (fileName.EndsWith(".cmfa", StringComparison.OrdinalIgnoreCase))
            contentType = "audio/mp4";
        else if (fileName.EndsWith(".cmfv", StringComparison.OrdinalIgnoreCase))
            contentType = "video/mp4";
        else
            contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

        context.Response.ContentType = contentType;
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        context.Response.Headers["Accept-Ranges"] = "bytes";

        if (response.Content.Headers.ContentLength.HasValue)
            context.Response.ContentLength64 = response.Content.Headers.ContentLength.Value;

        if (response.Content.Headers.TryGetValues("Content-Range", out var cr))
            context.Response.Headers["Content-Range"] = string.Join(", ", cr);

        await using (var resStream = context.OpenResponseStream())
        await using (var srcStream = await response.Content.ReadAsStreamAsync())
        {
            await srcStream.CopyToAsync(resStream);
        }

        context.SetHandled();
    }

    private static async Task SendStringWithLengthAsync(IHttpContext context, string text, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        await using var os = context.OpenResponseStream();
        await os.WriteAsync(bytes);
        context.SetHandled();
    }

    public static async Task HandleTempVideoAsync(IHttpContext context, string videoId)
    {
        if (!Sessions.TryGetValue(videoId, out var session) ||
            string.IsNullOrEmpty(session.TempFilePath) ||
            !File.Exists(session.TempFilePath))
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Temp video not found", "text/plain");
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
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        context.Response.ContentType = contentType;

        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = totalLength;
            await using var fs = File.OpenRead(filePath);
            await using var os = context.OpenResponseStream();
            await fs.CopyToAsync(os);
            context.SetHandled();
            return;
        }

        var rangeValue = rangeHeader["bytes=".Length..].Trim();
        var parts = rangeValue.Split('-');
        long start = 0;
        var end = totalLength - 1;

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

        await using (var fs = File.OpenRead(filePath))
        await using (var os = context.OpenResponseStream())
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

    public static string RecreateMasterPlaylist(string masterText, string masterUrl, NicoSession session, string baseUrl)
    {
        var lines = masterText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var baseUri = new Uri(masterUrl);

        var audioTracks = new List<(string GroupId, string Name, string Uri)>();
        var videoStreams = new List<(long Bandwidth, string StreamInf, string StreamUrl)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("#EXT-X-MEDIA:TYPE=AUDIO", StringComparison.OrdinalIgnoreCase))
            {
                var match = AudioMediaRegex().Match(line);
                if (match.Success)
                {
                    var groupId = match.Groups[1].Value;
                    var name = match.Groups[2].Value;
                    var uri = match.Groups[4].Value;
                    audioTracks.Add((groupId, name, uri));
                }
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
            {
                var match = StreamInfRegex().Match(line);
                if (match.Success && i + 1 < lines.Length)
                {
                    var bwStr = match.Groups[2].Value;
                    long.TryParse(bwStr, out var bw);
                    var nextLine = lines[i + 1].Trim();
                    if (!nextLine.StartsWith('#') && !string.IsNullOrWhiteSpace(nextLine))
                    {
                        videoStreams.Add((bw, line, nextLine));
                        i++;
                    }
                }
            }
        }

        // Select highest video stream
        var bestVideo = videoStreams.OrderByDescending(v => v.Bandwidth).FirstOrDefault();
        if (bestVideo.StreamUrl != null)
            session.SelectedVideoPlaylistUrl = new Uri(baseUri, bestVideo.StreamUrl).AbsoluteUri;

        // Find matching or highest audio track
        string? targetAudioGroup = null;
        if (bestVideo.StreamInf != null)
        {
            var audioMatch = AudioGroupAttrRegex().Match(bestVideo.StreamInf);
            if (audioMatch.Success)
                targetAudioGroup = audioMatch.Groups[1].Value;
        }

        var selectedAudio = audioTracks.FirstOrDefault(a => a.GroupId == targetAudioGroup);
        if (selectedAudio.Uri == null)
            selectedAudio = audioTracks.FirstOrDefault();

        if (selectedAudio.Uri != null)
            session.SelectedAudioPlaylistUrl = new Uri(baseUri, selectedAudio.Uri).AbsoluteUri;

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine("#EXT-X-INDEPENDENT-SEGMENTS");

        if (session.SelectedAudioPlaylistUrl != null)
            sb.AppendLine(
                $"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Main Audio\",DEFAULT=YES,URI=\"{baseUrl}/nico/{session.VideoId}/audio.m3u8\"");

        if (bestVideo.StreamInf != null)
        {
            var inf = bestVideo.StreamInf;
            inf = AudioGroupAttrRegex().Replace(inf, "AUDIO=\"audio\"");
            sb.AppendLine(inf);
            sb.AppendLine($"{baseUrl}/nico/{session.VideoId}/video.m3u8");
        }

        return sb.ToString();
    }

    public static string RewriteVariantPlaylist(string playlistText, string playlistUrl, NicoSession session, string baseUrl, bool isAudio)
    {
        var lines = playlistText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var sb = new StringBuilder(playlistText.Length + 512);
        var baseUri = new Uri(playlistUrl);
        var defaultExt = isAudio ? ".cmfa" : ".cmfv";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(rawLine);
                continue;
            }

            if (line.StartsWith('#'))
            {
                if (line.Contains("URI=\"", StringComparison.OrdinalIgnoreCase))
                {
                    var replaced = GenericUriAttrRegex().Replace(line, match =>
                    {
                        var uriValue = match.Groups[1].Value;
                        if (Uri.TryCreate(baseUri, uriValue, out var resolvedUri))
                        {
                            var ext = uriValue.Contains(".key", StringComparison.OrdinalIgnoreCase) ? ".key" : defaultExt;
                            var token = session.GetOrAddUrlToken(resolvedUri.AbsoluteUri, ext);
                            var proxied = $"{baseUrl}/nico/{session.VideoId}/proxy/{token}";
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
                if (Uri.TryCreate(baseUri, line, out var resolvedUri))
                {
                    var token = session.GetOrAddUrlToken(resolvedUri.AbsoluteUri, defaultExt);
                    sb.AppendLine($"{baseUrl}/nico/{session.VideoId}/proxy/{token}");
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
