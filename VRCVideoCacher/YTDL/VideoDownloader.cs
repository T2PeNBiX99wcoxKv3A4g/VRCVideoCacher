using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Jeek.Avalonia.Localization;
using JetBrains.Annotations;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL;

public partial class VideoDownloader : Singleton<VideoDownloader>
{
    private const string TempDownloadMp4Name = "_tempVideo.mp4";
    private const string TempDownloadMkvName = "_tempVideo.mkv";

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        }
    };

    private readonly LinkedList<VideoInfo> _downloadQueue = new();
    private readonly Lock _queueLock = new();
    private readonly ConcurrentDictionary<string, List<TaskCompletionSource<bool>>> _waiters = new();

    // Current download tracking
    private VideoInfo? _currentDownload;
    private volatile StatusActivity? _currentActivity;

    public VideoDownloader()
    {
        Task.Run(DownloadThread);
    }

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool>? OnDownloadCompleted;
    public static event Action? OnQueueChanged;

    private static string GetDownloadKey(VideoInfo info) => $"{info.VideoId}_{info.DownloadFormat}";

    private async Task DownloadThread()
    {
        while (!ChildProcessTracker.Terminating)
        {
            if (ChildProcessTracker.Terminating) break;
            await Task.Delay(100);

            VideoInfo? queueItem;
            lock (_queueLock)
            {
                if (_downloadQueue.Count == 0)
                {
                    _currentDownload = null;
                    continue;
                }

                queueItem = _downloadQueue.First?.Value;
                if (queueItem == null)
                    continue;

                _downloadQueue.RemoveFirst();
                _currentDownload = queueItem;
            }

            OnDownloadStarted?.Invoke(queueItem);

            // Real progress is reported into this activity: yt-dlp's own "[download] x%" output for
            // subprocess downloads, and a ProgressStream for the plain-HTTP ones.
            using var activity = StatusService.Begin(StatusCategory.Downloading,
                string.Format(Localizer.Get("StatusDownloading"), queueItem.VideoId));
            _currentActivity = activity;

            var success = await Try.Run(async () => queueItem.UrlType switch
            {
                UrlType.YouTube => await DownloadYouTubeVideo(queueItem, activity),
                UrlType.PyPyDance => await DownloadVideoWithId(queueItem, activity),
                UrlType.VRDancing => await DownloadVRDancingVideoWithId(queueItem, activity),
                UrlType.NicoVideo => await DownloadNicoVideo(queueItem, activity),
                UrlType.Other => await DownloadGenericVideo(queueItem, activity),
                _ => throw new ArgumentOutOfRangeException()
            }).GetOrElse(ex =>
            {
                Log.Error(ex, "Exception during download: {Ex}", ex.ToString());
                return Task.FromResult(false);
            });

            _currentActivity = null;

            var key = GetDownloadKey(queueItem);
            if (_waiters.TryRemove(key, out var tcsList))
                lock (tcsList)
                    foreach (var tcs in tcsList)
                        tcs.TrySetResult(success);

            OnDownloadCompleted?.Invoke(queueItem, success);
            OnQueueChanged?.Invoke();
            _currentDownload = null;
        }
    }

    [PublicAPI]
    public void QueueDownload2(VideoInfo videoInfo, bool highPriority = false)
    {
        lock (_queueLock)
        {
            if (_currentDownload != null &&
                _currentDownload.VideoId == videoInfo.VideoId &&
                _currentDownload.DownloadFormat == videoInfo.DownloadFormat)
                return;

            var existingNode = _downloadQueue.First;
            while (existingNode != null)
            {
                if (existingNode.Value.VideoId == videoInfo.VideoId &&
                    existingNode.Value.DownloadFormat == videoInfo.DownloadFormat)
                {
                    if (highPriority && existingNode != _downloadQueue.First)
                    {
                        _downloadQueue.Remove(existingNode);
                        _downloadQueue.AddFirst(videoInfo);
                        OnQueueChanged?.Invoke();
                    }

                    return;
                }

                existingNode = existingNode.Next;
            }

            if (highPriority)
                _downloadQueue.AddFirst(videoInfo);
            else
                _downloadQueue.AddLast(videoInfo);

            OnQueueChanged?.Invoke();
        }
    }

    [PublicAPI]
    public async Task<bool> DownloadAndWaitAsync2(VideoInfo videoInfo, TimeSpan timeout, bool highPriority = true)
    {
        var ext = videoInfo.DownloadFormat.ToString().ToLower();
        var fileName = $"{videoInfo.VideoId}.{ext}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
            return true;

        if (videoInfo.DownloadFormat != DownloadFormat.MP4)
        {
            // Any container can be served to AVPro: an MP4 cache copy (SABR convergence writes one)
            // satisfies this download too.
            var mp4Path = Path.Join(CacheManager.CachePath, $"{videoInfo.VideoId}.mp4");
            if (File.Exists(mp4Path))
                return true;
        }

        var key = GetDownloadKey(videoInfo);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var list = _waiters.GetOrAdd(key, _ => []);
        lock (list)
            list.Add(tcs);

        QueueDownload2(videoInfo, highPriority);

        if (File.Exists(filePath))
        {
            if (_waiters.TryGetValue(key, out var existingList))
                lock (existingList)
                    existingList.Remove(tcs);

            return true;
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cts.Token));
            if (completedTask == tcs.Task)
            {
                await cts.CancelAsync();
                return await tcs.Task;
            }

            Log.Warning("DownloadAndWaitAsync timed out for {VideoId}", videoInfo.VideoId);
            return false;
        }
        finally
        {
            if (_waiters.TryGetValue(key, out var existingList))
                lock (existingList)
                    existingList.Remove(tcs);
        }
    }

    [PublicAPI]
    public void ClearQueue2()
    {
        lock (_queueLock)
        {
            _downloadQueue.Clear();
            OnQueueChanged?.Invoke();
        }
    }

    // Public accessors for UI
    [PublicAPI]
    public IReadOnlyList<VideoInfo> GetQueueSnapshot2()
    {
        lock (_queueLock)
            return [.. _downloadQueue];
    }

    [PublicAPI]
    public int GetQueueCount2()
    {
        lock (_queueLock)
            return _downloadQueue.Count;
    }

    [PublicAPI]
    public VideoInfo? GetCurrentDownload2() => _currentDownload;

    /// <summary>Completion fraction (0..1) of the download in progress, or null when unknown/idle.</summary>
    [PublicAPI]
    public double? GetCurrentDownloadProgress2() => _currentActivity?.Progress;

    /// <summary>Streams a yt-dlp process' stdout+stderr, reporting download progress and returning the
    /// captured error output (progress lines excluded). Must be awaited before reading <c>ExitCode</c>.</summary>
    private static async Task<string> PumpYtdlpAsync(Process process, YtdlProgress progress)
    {
        var error = new StringBuilder();

        async Task PumpAsync(StreamReader reader, bool capture)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (capture && !line.StartsWith("[download]", StringComparison.Ordinal))
                    error.AppendLine(line);
                progress.Feed(line);
            }
        }

        await Task.WhenAll(PumpAsync(process.StandardOutput, false), PumpAsync(process.StandardError, true));
        return error.ToString().Trim();
    }

    private async Task<bool> DownloadYouTubeVideo(VideoInfo videoInfo, StatusActivity activity)
    {
        var url = videoInfo.VideoUrl;

        // Don't run yt-dlp against a video we already know is gone — both to spare the download the two
        // YouTube hits below (id lookup + download) and to avoid the failure spam that gets us bot-checked.
        if (UnavailableVideoCache.IsUnavailable(videoInfo.VideoId))
        {
            Log.Information("Skipping download of known-unavailable YouTube video {VideoId}", videoInfo.VideoId);
            return false;
        }

        var videoId = await Try.Run<string?>(async () => await VideoId.TryGetYouTubeVideoId(url)).GetOrElse(ex =>
        {
            Log.Error(ex, "Not downloading YouTube video: {Url} {Ex}", url, ex.ToString());
            return Task.FromResult<string?>(null);
        });

        if (string.IsNullOrEmpty(videoId))
        {
            Log.Warning("Invalid YouTube URL: {Url}", url);
            return false;
        }

        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);
        var tempDownloadMkvPath = Path.Join(tempDir.FullName, TempDownloadMkvName);

        var args = new List<string>();

        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = YtdlManager.YtdlPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var maxHeight = ConfigManager.Config.YouTubeMaxResolution;

        if (videoInfo.DownloadFormat == DownloadFormat.MKV)
        {
            // process.StartInfo.Arguments = $"-q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<={maxHeight}][vcodec~='^(avc|h264)']+ba[ext=m4a]/bv*[height<={maxHeight}][vcodec!=av01][vcodec!=vp9.2][protocol^=http]\" --remux-video mp4 {additionalArgs} -- \"{videoId}\"";
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[acodec~='^(mp4a|opus)']"
                : $"+(ba[language={ConfigManager.Config.YtdlpDubLanguage}]/ba)[acodec~='^(mp4a|opus)']";

            var formatString = "";

            if (ConfigManager.Config.YtdlpAllowAV1)
                formatString += $"bv*[height<={maxHeight}][vcodec~='^av01'][dynamic_range='SDR']{audioArg}/";

            if (ConfigManager.Config.AllowVP9)
                formatString += $"bv*[height<={maxHeight}][vcodec~='^(vp9|vp09)'][dynamic_range='SDR']{audioArg}/";

            // Always allow downloading H.264 as a backup. If vp9 is disabled we need this,
            // otherwise we might not be able to download videos without AV1 formats
            // For the SABR branch of yt-dlp, we don't need a trailing /b here, as we should
            // always be able to use SABR to download a preferable split format.
            formatString += $"bv*[height<={maxHeight}][vcodec~='^(h264|avc1)'][dynamic_range='SDR']{audioArg}";

            args.Add($"-o \"{tempDownloadMkvPath}\"");
            args.Add($"-f \"{formatString}\"");
            args.Add($"--remux-video mkv");
        }
        else
        {
            // Potato mode.
            var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[ext=m4a]"
                : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";
            args.Add($"-o \"{tempDownloadMp4Path}\"");
            var potatoMaxHeight = Math.Clamp(maxHeight, 360, 1080);
            args.Add(
                $"-f \"bv*[height<={potatoMaxHeight}][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<={potatoMaxHeight}][vcodec~='^av01'][dynamic_range='SDR']{audioArgPotato}\"");
            args.Add("--remux-video mp4");
            // $@"-f best/bestvideo[height<=?720]+bestaudio {url} " %(id)s.%(ext)s
        }

        process.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"-- \"{videoId}\"");
        Log.Information("Downloading YouTube Video: {Args}", process.StartInfo.Arguments);

        var progress = new YtdlProgress(activity);

        // yt-dlp rewrites the cookie jar on exit; overlapping this download with a URL resolution
        // corrupts the session and gets us bot-checked. See YtdlCookieJar.
        string error;
        using (await YtdlCookieJar.AcquireAsync())
        {
            process.Start();
            using (var processTracker = ChildProcessTracker.Tracking(process))
            {
                if (processTracker.TrackResult == ChildProcessTracker.TrackResult.Terminating) return false;
                error = await PumpYtdlpAsync(process, progress);
                await process.WaitForExitAsync();
            }
        }

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {ExitCode} {Url} {Error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error(
                    "Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }

        await Task.Delay(100);

        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            Try.Run(() =>
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
                if (File.Exists(tempDownloadMkvPath))
                    File.Delete(tempDownloadMkvPath);
            }).OnFailure(ex => Log.Error(ex, "Failed to delete temp file: {Ex}", ex.ToString()));
            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else if (File.Exists(tempDownloadMkvPath))
            File.Move(tempDownloadMkvPath, filePath);
        else
        {
            Log.Error("Failed to download YouTube Video: {Url}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("YouTube Video Downloaded: {Url}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadVRDancingVideoWithId(VideoInfo videoInfo, StatusActivity activity)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        var url = videoInfo.VideoUrl;
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
            Arguments = $"-q -o \"{tempDownloadMp4Path}\" --remux-video mp4 \"{url}\""
        };
        Log.Information("Downloading VRDancing Video: {Args}", process.StartInfo.Arguments);
        process.Start();
        var progress = new YtdlProgress(activity);
        var error = await ChildProcessTracker.Tracking(process, async result =>
        {
            if (result == ChildProcessTracker.TrackResult.Terminating) return "";
            var output = await PumpYtdlpAsync(process, progress);
            await process.WaitForExitAsync();
            return output;
        });

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download VRDancing Video: {ExitCode} {Url} {Error}", process.ExitCode, url, error);
            return false;
        }

        await Task.Delay(100);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            Try.Run(() =>
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }).OnFailure(ex => Log.Error(ex, "Failed to delete temp file: {Ex}", ex.ToString()));
            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else
        {
            Log.Error("Failed to download VRDancing Video: {Url}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("VRDancing Video Downloaded: {Url}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadVideoWithId(VideoInfo videoInfo, StatusActivity activity)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        Log.Information("Downloading Video: {Url}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;
        var response = await HttpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Log.Information("Redirected to: {Url}", response.Headers.Location);
            url = response.Headers.Location?.ToString();
            response = await HttpClient.GetAsync(url);
        }

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to download video: {Url}", url);
            return false;
        }

        await using (var stream = new ProgressStream(
                         await response.Content.ReadAsStreamAsync(),
                         response.Content.Headers.ContentLength,
                         activity.Report,
                         TimeSpan.FromSeconds(60)))
        await using (var fileStream =
                     new FileStream(tempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None))
            await stream.CopyToAsync(fileStream);
        response.TryDispose();
        await Task.Delay(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            Try.Run(() =>
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }).OnFailure(ex => Log.Error(ex, "Failed to delete temp file: {Ex}", ex.ToString()));
            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else
        {
            Log.Error("Failed to download Video: {Url}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("Video Downloaded: {Url}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadGenericVideo(VideoInfo videoInfo, StatusActivity activity)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        var url = videoInfo.VideoUrl;
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
            Arguments = $"-q -o \"{tempDownloadMp4Path}\" --remux-video mp4 \"{url}\""
        };

        Log.Information("Downloading Generic Video: {Args}", process.StartInfo.Arguments);
        process.Start();
        var progress = new YtdlProgress(activity);
        var error = await ChildProcessTracker.Tracking(process, async result =>
        {
            if (result == ChildProcessTracker.TrackResult.Terminating) return "";
            var output = await PumpYtdlpAsync(process, progress);
            await process.WaitForExitAsync();
            return output;
        });

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download Generic Video: {ExitCode} {Url} {Error}", process.ExitCode, url, error);
            return false;
        }

        await Task.Delay(100);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            Try.Run(() =>
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }).OnFailure(ex => Log.Error(ex, "Failed to delete temp file: {Ex}", ex.ToString()));
            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
            File.Move(tempDownloadMp4Path, filePath);
        else
        {
            Log.Error("Failed to download Generic Video: {Url}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("Generic Video Downloaded: {Url}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private async Task<bool> DownloadNicoVideo(VideoInfo videoInfo, StatusActivity activity)
    {
        using var tempDir = new TempDir();
        var tempDownloadPath = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        var url = videoInfo.VideoUrl;
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
            // NicoVideo playback only supports MP4.
            Arguments = $"-q -o \"{tempDownloadPath}\" --remux-video mp4 \"{url}\""
        };

        Log.Information("Downloading NicoNico Video: {Args}", process.StartInfo.Arguments);
        process.Start();
        var progress = new YtdlProgress(activity);
        var error = await ChildProcessTracker.Tracking(process, async result =>
        {
            if (result == ChildProcessTracker.TrackResult.Terminating) return "";
            var output = await PumpYtdlpAsync(process, progress);
            await process.WaitForExitAsync();
            return output;
        });

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download NicoNico Video: {ExitCode} {Url} {Error}", process.ExitCode, url, error);
            return false;
        }

        await Task.Delay(100);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            Try.Run(() =>
            {
                if (File.Exists(tempDownloadPath))
                    File.Delete(tempDownloadPath);
            }).OnFailure(ex => Log.Error(ex, "Failed to delete temp file: {Ex}", ex.ToString()));
            return false;
        }

        if (File.Exists(tempDownloadPath))
            File.Move(tempDownloadPath, filePath);
        else
        {
            Log.Error("Failed to download NicoNico Video: {Url}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("NicoNico Video Downloaded: {Url}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }
}