using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL;

public class VideoDownloader : Singleton<VideoDownloader>
{
    private const string TempDownloadMp4Name = "_tempVideo.mp4";
    private const string TempDownloadWebmName = "_tempVideo.webm";

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        }
    };

    private readonly ConcurrentQueue<VideoInfo> _downloadQueue = new();

    // Current download tracking
    private VideoInfo? _currentDownload;

    public VideoDownloader()
    {
        Task.Run(DownloadThread);
    }

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool>? OnDownloadCompleted;
    public static event Action? OnQueueChanged;

    private async Task DownloadThread()
    {
        while (true)
        {
            await Task.Delay(100);
            if (_downloadQueue.IsEmpty)
            {
                _currentDownload = null;
                continue;
            }

            _downloadQueue.TryDequeue(out var queueItem);
            if (queueItem == null)
                continue;

            _currentDownload = queueItem;
            OnDownloadStarted?.Invoke(queueItem);

            // Indeterminate: the download runs through a yt-dlp subprocess (YouTube) or a redirected HTTP
            // fetch, neither of which reports a clean byte total here. Shows a spinner + which video.
            using var activity = StatusService.Begin(StatusCategory.Downloading,
                string.Format(Localizer.Get("StatusDownloading"), queueItem.VideoId));

            var success = false;
            try
            {
                success = queueItem.UrlType switch
                {
                    UrlType.YouTube => await DownloadYouTubeVideo(queueItem),
                    UrlType.PyPyDance => await DownloadVideoWithId(queueItem),
                    UrlType.VRDancing => await DownloadVRDancingVideoWithId(queueItem),
                    UrlType.Other => await DownloadGenericVideo(queueItem),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            catch (Exception ex)
            {
                Log.Error("Exception during download: {Ex}", ex.ToString());
                success = false;
            }

            OnDownloadCompleted?.Invoke(queueItem, success);
            OnQueueChanged?.Invoke();
            _currentDownload = null;
        }
    }

    public void QueueDownload(VideoInfo videoInfo)
    {
        if (_downloadQueue.Any(x => x.VideoId == videoInfo.VideoId &&
                                   x.DownloadFormat == videoInfo.DownloadFormat))
            // Log.Information("URL is already in the download queue.");
            return;
        if (_currentDownload != null &&
            _currentDownload.VideoId == videoInfo.VideoId &&
            _currentDownload.DownloadFormat == videoInfo.DownloadFormat)
            // Log.Information("URL is already being downloaded.");
            return;

        _downloadQueue.Enqueue(videoInfo);
        OnQueueChanged?.Invoke();
    }

    public void ClearQueue()
    {
        _downloadQueue.Clear();
        OnQueueChanged?.Invoke();
    }

    // Public accessors for UI
    public IReadOnlyList<VideoInfo> GetQueueSnapshot() => _downloadQueue.ToArray();
    public int GetQueueCount() => _downloadQueue.Count;
    public VideoInfo? GetCurrentDownload() => _currentDownload;

    private async Task<bool> DownloadYouTubeVideo(VideoInfo videoInfo)
    {
        var url = videoInfo.VideoUrl;

        // Don't run yt-dlp against a video we already know is gone — both to spare the download the two
        // YouTube hits below (id lookup + download) and to avoid the failure spam that gets us bot-checked.
        if (UnavailableVideoCache.IsUnavailable(videoInfo.VideoId))
        {
            Log.Information("Skipping download of known-unavailable YouTube video {VideoId}", videoInfo.VideoId);
            return false;
        }

        string? videoId;
        try
        {
            videoId = await VideoId.TryGetYouTubeVideoId(url);
            if (string.IsNullOrEmpty(videoId))
            {
                Log.Warning("Invalid YouTube URL: {URL}", url);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Not downloading YouTube video: {URL} {ex}", url, ex.ToString());
            return false;
        }

        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);
        var tempDownloadWebmPath = Path.Join(tempDir.FullName, TempDownloadWebmName);

        var args = new List<string>
        {
            "-q"
        };

        using var process = new Process();
        process.StartInfo.FileName = YtdlManager.Instance.YtdlPath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

        if (videoInfo.DownloadFormat == DownloadFormat.Webm)
        {
            // process.StartInfo.Arguments = $"-q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^(avc|h264)']+ba[ext=m4a]/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec!=av01][vcodec!=vp9.2][protocol^=http]\" --remux-video mp4 {additionalArgs} -- \"{videoId}\"";
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[acodec=opus][ext=webm]"
                : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[acodec=opus][ext=webm])";
            args.Add($"-o \"{tempDownloadWebmPath}\"");
            args.Add(
                $"-f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\"");
        }
        else
        {
            // Potato mode.
            var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[ext=m4a]"
                : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";
            args.Add($"-o \"{tempDownloadMp4Path}\"");
            args.Add(
                $"-f \"bv*[height<=1080][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<=1080][vcodec~='^av01'][dynamic_range='SDR']\"");
            args.Add("--remux-video mp4");
            // $@"-f best/bestvideo[height<=?720]+bestaudio {url} " %(id)s.%(ext)s
        }

        process.StartInfo.Arguments = YtdlManager.Instance.GenerateYtdlArgs(args, $"-- \"{videoId}\"");
        Log.Information("Downloading YouTube Video: {Args}", process.StartInfo.Arguments);

        // yt-dlp rewrites the cookie jar on exit; overlapping this download with a URL resolution
        // corrupts the session and gets us bot-checked. See YtdlCookieJar.
        string error;
        using (await YtdlCookieJar.AcquireAsync())
        {
            process.Start();
            ChildProcessTracker.Instance.Track(process);
            try
            {
                await process.WaitForExitAsync();
                error = (await process.StandardError.ReadToEndAsync()).Trim();
            }
            catch
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch
                {
                    /* best effort */
                }

                throw;
            }
            finally
            {
                ChildProcessTracker.Instance.Untrack(process);
            }
        }

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error(
                    "Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }

        Thread.Sleep(100);

        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.Instance.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
                if (File.Exists(tempDownloadWebmPath))
                    File.Delete(tempDownloadWebmPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }

            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else if (File.Exists(tempDownloadWebmPath))
            File.Move(tempDownloadWebmPath, filePath);
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return false;
        }

        CacheManager.Instance.AddToCache(fileName);
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadVRDancingVideoWithId(VideoInfo videoInfo)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        var url = videoInfo.VideoUrl;
        using var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.Instance.YtdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.Arguments = $"-q -o \"{tempDownloadMp4Path}\" --remux-video mp4 \"{url}\"";
        Log.Information("Downloading VRDancing Video: {Args}", process.StartInfo.Arguments);
        process.Start();
        ChildProcessTracker.Instance.Track(process);
        string error;
        try
        {
            await process.WaitForExitAsync();
            error = (await process.StandardError.ReadToEndAsync()).Trim();
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
                /* best effort */
            }

            throw;
        }
        finally
        {
            ChildProcessTracker.Instance.Untrack(process);
        }

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download VRDancing Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            return false;
        }

        Thread.Sleep(100);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.Instance.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }

            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else
        {
            Log.Error("Failed to download VRDancing Video: {URL}", url);
            return false;
        }

        CacheManager.Instance.AddToCache(fileName);
        Log.Information("VRDancing Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadVideoWithId(VideoInfo videoInfo)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;
        var response = await HttpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Log.Information("Redirected to: {URL}", response.Headers.Location);
            url = response.Headers.Location?.ToString();
            response = await HttpClient.GetAsync(url);
        }

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to download video: {URL}", url);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream =
            new FileStream(tempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        fileStream.Close();
        response.Dispose();
        await Task.Delay(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.Instance.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }

            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else
        {
            Log.Error("Failed to download Video: {URL}", url);
            return false;
        }

        CacheManager.Instance.AddToCache(fileName);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadGenericVideo(VideoInfo videoInfo)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        var url = videoInfo.VideoUrl;
        using var process = new Process();
        process.StartInfo.FileName = YtdlManager.Instance.YtdlPath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.Arguments = $"-q -o \"{tempDownloadMp4Path}\" --remux-video mp4 \"{url}\"";
        Log.Information("Downloading Generic Video: {Args}", process.StartInfo.Arguments);
        process.Start();
        ChildProcessTracker.Instance.Track(process);
        string error;
        try
        {
            await process.WaitForExitAsync();
            error = (await process.StandardError.ReadToEndAsync()).Trim();
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
                /* best effort */
            }

            throw;
        }
        finally
        {
            ChildProcessTracker.Instance.Untrack(process);
        }

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download Generic Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            return false;
        }

        Thread.Sleep(100);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.Instance.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }

            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else
        {
            Log.Error("Failed to download Generic Video: {URL}", url);
            return false;
        }

        CacheManager.Instance.AddToCache(fileName);
        Log.Information("Generic Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }
}