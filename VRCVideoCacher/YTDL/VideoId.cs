using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL.SiteHandlers;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VRCVideoCacher.YTDL;

public partial class VideoId : Singleton<VideoId>
{
    internal static Uri? ToUri(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

    internal static string HashUrl(string url) =>
        Convert.ToBase64String(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(url)))
            .Replace("/", "")
            .Replace("+", "")
            .Replace("=", "");

    private static Process GetYtdlpProcess()
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        return process;
    }

    private async Task<(string Output, string Error, int ExitCode)> RunYtdlpAsync(List<string> args, string url)
    {
        using var ytdlpProcess = GetYtdlpProcess();
        ytdlpProcess.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"\"{url}\"");

        // yt-dlp rewrites the cookie jar on exit; overlapping it with the download queue corrupts the
        // session and gets us bot-checked. See YtdlCookieJar.
        using var cookieJar = await YtdlCookieJar.AcquireAsync();

        Log.Information("Starting yt-dlp with args: {Args:l}", ytdlpProcess.StartInfo.Arguments);
        ytdlpProcess.Start();
        using (ChildProcessTracker.Tracking(ytdlpProcess))
        {
            var outputTask = ytdlpProcess.StandardOutput.ReadToEndAsync();
            var errorTask = ytdlpProcess.StandardError.ReadToEndAsync();
            var output = await outputTask;
            var error = await errorTask;
            await ytdlpProcess.WaitForExitAsync();
            Log.Information("Finished yt-dlp");
            return (output.Trim(), error.Trim(), ytdlpProcess.ExitCode);
        }
    }

    [PublicAPI]
    public async Task<VideoInfo?> GetVideoId2(string url, bool avPro)
    {
        url = url.Trim();
        url = await SiteHandlerRegistry.ApplyRewrites(url);

        var uri = ToUri(url);
        if (uri == null) return null;

        var handler = SiteHandlerRegistry.Resolve(uri);
        return handler == null ? null : await handler.GetVideoInfo(url, uri, avPro);
    }

    [PublicAPI]
    public async Task<string> TryGetYouTubeVideoId2(string url)
    {
        var args = new List<string>
        {
            "-j"
        };

        var (rawData, error, exitCode) = await RunYtdlpAsync(args, url);
        if (exitCode != 0)
        {
            Log.Warning("Failed to get video ID: {Error}", error.Trim());
            return string.Empty;
        }

        if (string.IsNullOrEmpty(rawData))
        {
            Log.Warning("Failed to get video ID");
            return string.Empty;
        }

        var data = JsonSerializer.Deserialize(rawData, VideoIdJsonContext.Default.YtdlpVideoInfo);
        if (data?.Id is null || data.Duration is null)
        {
            Log.Warning("Failed to get video ID");
            return string.Empty;
        }

        await DatabaseManager.AddVideoInfoCacheAsync(new()
        {
            Id = data.Id,
            Title = data.Name,
            Author = data.Author,
            Duration = data.Duration,
            Type = UrlType.YouTube
        });

        if (data.IsLive == true)
        {
            Log.Warning("Failed to get video ID: Video is a stream");
            return string.Empty;
        }

        // ReSharper disable once InvertIf
        if (data.Duration > ConfigManager.Config.CacheYouTubeMaxLength * 60)
        {
            Log.Warning("Failed to get video ID: Video is longer than configured max length ({Length}s > {Max}s)",
                data.Duration, ConfigManager.Config.CacheYouTubeMaxLength * 60);
            return string.Empty;
        }

        return data.Id;
    }

    [PublicAPI]
    public async Task<string> GetURLResonite2(VideoInfo videoInfo)
    {
        // Don't hit YouTube for a video we already know is gone — a looping player would otherwise get us
        // bot-checked. Resonite treats an empty response as "no video", which is what we want here.
        if (videoInfo.UrlType == UrlType.YouTube && UnavailableVideoCache.IsUnavailable(videoInfo.VideoId))
            return string.Empty;

        var url = videoInfo.VideoUrl;
        var args = new List<string>();
        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage))
            args.Add($"-f \"[language={ConfigManager.Config.YtdlpDubLanguage}]\"");
        args.Add("--flat-playlist");
        args.Add("-i");
        args.Add("-J"); // --dump-single-json
        args.Add("-s");
        args.Add("--impersonate=\"safari\"");
        args.Add("--extractor-args=\"youtube:player_client=web;youtubetab:skip=authcheck\"");

        var (output, error, exitCode) = await RunYtdlpAsync(args, url);
        // ReSharper disable once InvertIf
        if (exitCode != 0)
        {
            // Remember a gone video so the next request short-circuits before touching YouTube.
            if (videoInfo.UrlType == UrlType.YouTube && UnavailableVideoCache.IsUnavailabilityError(error))
                UnavailableVideoCache.Mark(videoInfo.VideoId);
            else if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
                Log.Error("Fix this error by running cookie setup.");

            return string.Empty;
        }

        return output;
    }

    [PublicAPI]
    public async Task<Tuple<string, bool>> GetUrl2(VideoInfo videoInfo, bool avPro)
    {
        if (videoInfo.UrlType == UrlType.NicoVideo)
        {
            var nicoResult = await NicoVideoApiService.FetchVideoResult(videoInfo.VideoId);
            if (!string.IsNullOrEmpty(nicoResult?.StreamUrl))
                return new(nicoResult.StreamUrl, true);
        }

        // if url contains "results?" then it's a search
        if (videoInfo.VideoUrl.Contains("results?") && videoInfo.UrlType == UrlType.YouTube)
        {
            const string message = "URL is a search query, cannot get video URL.";
            return new(message, false);
        }

        var url = videoInfo.VideoUrl;
        var uri = ToUri(url);
        var handler = uri != null ? SiteHandlerRegistry.Resolve(uri) : null;
        var args = handler?.GetYtdlpArguments(uri!, avPro) ?? [];
        args.Add("--get-url");

        var (output, error, exitCode) = await RunYtdlpAsync(args, url);

        if (exitCode == 0) // success
            return new(output, true);

        if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
            Log.Error("Fix this error by running cookie setup.");

        // ReSharper disable once InvertIf
        if (error.Contains(
                "Requested format is not available. Use --list-formats for a list of available formats") && avPro)
        {
            Log.Warning("AVPro format request failed retrying for 360p.");
            // ReSharper disable once TailRecursiveCall
            return await GetUrl2(videoInfo, false);
        }

        return new(error, false);
    }
}