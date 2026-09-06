using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json;
using Serilog;
using SharpCompress.Readers;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL;

public class YtdlManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<YtdlManager>();

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        }
    };

    public static readonly string CookiesPath;

    public static readonly string YtdlPath =
        Path.Join(Program.UtilsPath, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");

    public static readonly string DenoPath =
        Path.Join(Program.UtilsPath, OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    public static readonly string FfmpegPath =
        Path.Join(Program.UtilsPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

    // The SABR-capable yt-dlp build, used as the ONLY yt-dlp. It is a superset of mainline: everything
    // that worked before still works, and SABR-only videos now extract and download too — which mainline
    // cannot do at all.
    //
    // Note this is a fixed tag, not /releases/latest: the build is a PRERELEASE, and /latest skips those.
    // Because the tag never changes ("sabr"), the up-to-date check must compare the release NAME
    // ("sabr 2026.07.11.051141"), not the tag, or we would download once and never update again.
    private const string YtdlpApiUrl = "https://api.github.com/repos/bashonly/yt-dlp/releases/tags/sabr";
    private const string FfmpegNightlyApiUrl = "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest";
    private const string FfmpegApiUrl = "https://api.github.com/repos/GyanD/codexffmpeg/releases/latest";
    private const string DenoApiUrl = "https://api.github.com/repos/denoland/deno/releases/latest";
    private const string DenoFallBackVersionURL = "https://dl.deno.land/release-latest.txt";
    private const string DenoFallBackDownloadURL = "https://dl.deno.land/release/";

    // Large tool downloads: retry the whole attempt a few times with a short backoff, and abort a single
    // attempt if the connection goes silent. ResponseHeadersRead means HttpClient.Timeout no longer bounds
    // the body, so the stall timeout is what stops a dead socket from hanging the download forever.
    private const int DownloadRetries = 3;
    private static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Runs a download attempt up to <see cref="DownloadRetries"/> times with a short linear backoff,
    /// rethrowing the last failure so the caller can fall back to another source (or log and move on). A
    /// download is safe to retry — each attempt writes to a fresh file.
    /// </summary>
    private static async Task RetryAsync(Func<Task> attempt, string what)
    {
        for (var i = 1;; i++)
            try
            {
                await attempt();
                return;
            }
            catch (Exception ex) when (i < DownloadRetries)
            {
                var delay = TimeSpan.FromSeconds(2 * i);
                Log.Warning(ex, "{What} failed (attempt {Attempt}/{Attempts}); retrying in {Delay:0}s",
                    what, i, DownloadRetries, delay.TotalSeconds);
                await Task.Delay(delay);
            }
    }


    static YtdlManager()
    {
        CookiesPath = Path.Join(Program.DataPath, "youtube_cookies.txt");

        // try to locate in PATH
        if (LaunchArgs.UseGlobalPath)
        {
            YtdlPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp") ??
                       throw new FileNotFoundException("Unable to find yt-dlp");
            DenoPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "deno.exe" : "deno") ??
                       throw new FileNotFoundException("Unable to find Deno runtime");
            FfmpegPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg") ??
                         string.Empty;
        }

        Log.Debug("Using ytdl path: {YtdlPath}", YtdlPath);
    }

    public static string GenerateYtdlArgs(List<string> args, string urlArg)
    {
        var globalArgs = new List<string>
        {
            "--encoding utf-8",
            "--ignore-config",
            "--no-playlist",
            "--no-warnings",
            "--no-mtime",
            "--no-progress"
        };
        args.AddRange(globalArgs);

        if (File.Exists(FfmpegPath))
            args.Add($"--ffmpeg-location \"{FfmpegPath}\"");

        if (File.Exists(DenoPath))
            args.Add($"--js-runtimes deno:\"{DenoPath}\"");
        else
            Log.Error("Deno runtime not found at path: {DenoPath}", DenoPath);

        if (Program.IsCookiesEnabledAndValid())
            args.Add($"--cookies \"{CookiesPath}\"");

        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpAdditionalArgs))
            args.Add(ConfigManager.Config.YtdlpAdditionalArgs);

        args.Add(urlArg);
        return string.Join(' ', args);
    }

    public static void StartYtdlUpdaterThread()
    {
        Task.Run(YtdlUpdaterTask);
    }

    private static async Task YtdlUpdaterTask()
    {
        const int interval = 60 * 60 * 1000; // 1 hour
        while (true)
        {
            await Task.Delay(interval);
            await VvcConfigService.GetConfig();
            await TryDownloadYtdlp();
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public static async Task TryDownloadYtdlp()
    {
        if (!Directory.Exists(Program.UtilsPath))
            throw new("Failed to get Utils path");

        Log.Information("Checking for YT-DLP updates...");
        using var response = await HttpClient.GetAsync(YtdlpApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Failed to check for YT-DLP updates.");
            return;
        }

        var data = await response.Content.ReadAsStringAsync();
        var json = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse YT-DLP update response.");
            return;
        }

        var currentYtdlVersion = Versions.CurrentVersion.Ytdlp;
        if (!File.Exists(YtdlPath))
            currentYtdlVersion = "Not Installed";
        else if (!await CheckIfProcessStarts(YtdlPath))
            currentYtdlVersion = "Not Working";

        // See YtdlpApiUrl: the tag is a constant ("sabr"), so the release name carries the real version.
        var latestVersion = string.IsNullOrEmpty(json.name) ? json.tag_name : json.name;
        Log.Information("YT-DLP Current: {Installed} Latest: {Latest}", currentYtdlVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for YT-DLP updates.");
            return;
        }

        if (currentYtdlVersion == latestVersion)
        {
            Log.Information("YT-DLP is up to date.");
            return;
        }

        Log.Information("YT-DLP is outdated. Updating...");

        await DownloadYtdl(json);
    }

    public static async Task TryDownloadDeno()
    {
        if (!Directory.Exists(Program.UtilsPath))
            throw new("Failed to get Utils path");

        using var apiResponse = await HttpClient.GetAsync(DenoApiUrl);
        if (!apiResponse.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest ffmpeg release: {ResponseStatusCode}", apiResponse.StatusCode);
            return;
        }

        var data = await apiResponse.Content.ReadAsStringAsync();
        var json = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse deno release response.");
            return;
        }

        var currentDenoVersion = Versions.CurrentVersion.Deno;
        if (!File.Exists(DenoPath))
            currentDenoVersion = "Not Installed";
        else if (!await CheckIfProcessStarts(DenoPath))
            currentDenoVersion = "Not Working";

        var latestVersion = json.tag_name;
        Log.Information("Deno Current: {Installed} Latest: {Latest}", currentDenoVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for Deno updates.");
            return;
        }

        if (currentDenoVersion == latestVersion)
        {
            Log.Information("Deno is up to date.");
            return;
        }

        Log.Information("Deno is outdated. Updating...");

        string assetName;
        if (OperatingSystem.IsWindows())
            assetName = "deno-x86_64-pc-windows-msvc.zip";
        else if (OperatingSystem.IsLinux())
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetName = "deno-x86_64-unknown-linux-gnu.zip";
                    break;
                case Architecture.Arm64:
                    assetName = "deno-aarch64-unknown-linux-gnu.zip";
                    break;
                default:
                    Log.Error("Unsupported architecture {OSArchitecture}", RuntimeInformation.OSArchitecture);
                    return;
            }
        else
        {
            Log.Error("Unsupported operating system {OperatingSystem}", Environment.OSVersion);
            return;
        }

        // deno-x86_64-pc-windows-msvc.zip -> deno-x86_64-pc-windows-msvc
        var assets = json.assets.Where(asset => asset.name == assetName).ToList();
        if (assets.Count < 1)
        {
            Log.Error("Unable to find Deno asset {AssetName} for this platform.", assetName);
            return;
        }

        Log.Information("Downloading Deno...");
        var url = assets.First().browser_download_url;

        using var activity = StatusService.Begin(StatusCategory.Provisioning,
            string.Format(Localizer.Get("StatusDownloading"), "Deno"), key: ToolVerifier.DenoKey);

        var report = activity.Report;

        async Task DownloadFromGithubAsync()
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var responseStream = new ProgressStream(
                await response.Content.ReadAsStreamAsync(), response.Content.Headers.ContentLength,
                report, DownloadStallTimeout);
            var reader = await ReaderFactory.OpenAsyncReader(responseStream);
            try
            {
                while (await reader.MoveToNextEntryAsync())
                {
                    if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                        continue;

                    Log.Debug("Extracting file {Name} ({Size} bytes)", reader.Entry.Key, reader.Entry.Size);
                    var path = Path.Join(Program.UtilsPath, reader.Entry.Key);
                    await using var outputStream =
                        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    await using var entryStream = await reader.OpenEntryStreamAsync();
                    await entryStream.CopyToAsync(outputStream);
                    FileTools.MarkFileExecutable(path);
                    Versions.CurrentVersion.Deno = json.tag_name;
                    Versions.Save();
                    Log.Information("Deno downloaded and extracted.");
                    return;
                }

                throw new("Deno archive contained no files.");
            }
            finally
            {
                await reader.DisposeAsync();
            }
        }

        try
        {
            await RetryAsync(DownloadFromGithubAsync, "Deno download");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Deno download from GitHub failed after retries; trying the fallback source.");
            await TryDownloadDenoFallback(assetName, activity);
        }
    }

    private static async Task TryDownloadDenoFallback(string assetName, StatusActivity activity)
    {
        Log.Warning("Falling back to Deno version check via text file.");
        using var response = await HttpClient.GetAsync(DenoFallBackVersionURL);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest Deno version: {ResponseStatusCode}", response.StatusCode);
            return;
        }

        var latestVersion = (await response.Content.ReadAsStringAsync()).Trim();
        var url = $"{DenoFallBackDownloadURL}{latestVersion}/{assetName}";

        async Task DownloadAsync()
        {
            using var downloadResponse = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            downloadResponse.EnsureSuccessStatusCode();
            await using var responseStream = new ProgressStream(
                await downloadResponse.Content.ReadAsStreamAsync(), downloadResponse.Content.Headers.ContentLength,
                activity.Report, DownloadStallTimeout);
            var reader = await ReaderFactory.OpenAsyncReader(responseStream);
            try
            {
                while (await reader.MoveToNextEntryAsync())
                {
                    if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                        continue;

                    Log.Debug("Extracting file {Name} ({Size} bytes)", reader.Entry.Key, reader.Entry.Size);
                    var path = Path.Join(Program.UtilsPath, reader.Entry.Key);
                    await using var outputStream =
                        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    await using var entryStream = await reader.OpenEntryStreamAsync();
                    await entryStream.CopyToAsync(outputStream);
                    FileTools.MarkFileExecutable(path);
                    Versions.CurrentVersion.Deno = latestVersion;
                    Versions.Save();
                    Log.Information("Deno downloaded and extracted.");
                    return;
                }

                throw new("Deno fallback archive contained no files.");
            }
            finally
            {
                await reader.DisposeAsync();
            }
        }

        try
        {
            await RetryAsync(DownloadAsync, "Deno fallback download");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download Deno from the fallback source after retries.");
        }
    }

    public static async Task TryDownloadFfmpeg()
    {
        if (!Directory.Exists(Program.UtilsPath))
            throw new("Failed to get Utils path");

        using var apiResponse =
            await HttpClient.GetAsync(OperatingSystem.IsWindows() ? FfmpegApiUrl : FfmpegNightlyApiUrl);
        if (!apiResponse.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest ffmpeg release: {ResponseStatusCode}", apiResponse.StatusCode);
            return;
        }

        var data = await apiResponse.Content.ReadAsStringAsync();
        var json = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse ffmpeg release response.");
            return;
        }

        var currentffmpegVersion = Versions.CurrentVersion.Ffmpeg;
        if (!File.Exists(FfmpegPath))
            currentffmpegVersion = "Not Installed";
        else if (!await CheckIfProcessStarts(FfmpegPath, "-version"))
            currentffmpegVersion = "Not Working";

        var latestVersion = OperatingSystem.IsWindows() ? json.tag_name : json.name;
        Log.Information("FFmpeg Current: {Installed} Latest: {Latest}", currentffmpegVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for FFmpeg updates.");
            return;
        }

        if (currentffmpegVersion == latestVersion)
        {
            Log.Information("FFmpeg is up to date.");
            return;
        }

        Log.Information("FFmpeg is outdated. Updating...");

        string assetSuffix;
        if (OperatingSystem.IsWindows())
            assetSuffix = "full_build-shared.zip";
        else if (OperatingSystem.IsLinux())
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetSuffix = "master-latest-linux64-gpl.tar.xz";
                    break;
                case Architecture.Arm64:
                    assetSuffix = "master-latest-linuxarm64-gpl.tar.xz";
                    break;
                default:
                    Log.Error("Unsupported architecture {OSArchitecture}", RuntimeInformation.OSArchitecture);
                    return;
            }
        else
        {
            Log.Error("Unsupported operating system {OperatingSystem}", Environment.OSVersion);
            return;
        }

        var url = json.assets
            .FirstOrDefault(assetVersion => assetVersion.name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase))
            ?.browser_download_url ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            Log.Error("Unable to find ffmpeg asset for this platform.");
            return;
        }

        Log.Information("Downloading FFmpeg...");

        using var activity = StatusService.Begin(StatusCategory.Provisioning,
            string.Format(Localizer.Get("StatusDownloading"), "FFmpeg"), key: ToolVerifier.FfmpegKey);

        var report = activity.Report;

        async Task DownloadAsync()
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var responseStream = new ProgressStream(
                await response.Content.ReadAsStreamAsync(), response.Content.Headers.ContentLength,
                report, DownloadStallTimeout);
            var reader = await ReaderFactory.OpenAsyncReader(responseStream);
            var success = false;
            try
            {
                while (await reader.MoveToNextEntryAsync())
                {
                    if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                        continue;

                    if (!reader.Entry.Key.Contains("/bin/"))
                        continue;

                    var fileName = Path.GetFileName(reader.Entry.Key);
                    Log.Debug("Extracting file {Name} ({Size} bytes)", fileName, reader.Entry.Size);
                    var path = Path.Join(Program.UtilsPath, fileName);
                    await using var outputStream =
                        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    await using var entryStream = await reader.OpenEntryStreamAsync();
                    await entryStream.CopyToAsync(outputStream);
                    FileTools.MarkFileExecutable(path);
                    success = true;
                }
            }
            finally
            {
                await reader.DisposeAsync();
            }

            if (!success)
                throw new("Failed to extract ffmpeg files.");
        }

        try
        {
            await RetryAsync(DownloadAsync, "FFmpeg download");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FFmpeg download failed after retries.");
            return;
        }

        Versions.CurrentVersion.Ffmpeg = latestVersion;
        Versions.Save();
        Log.Information("FFmpeg downloaded and extracted.");
    }

    private static async Task DownloadYtdl(GitHubRelease json)
    {
        if (File.Exists(YtdlPath) && File.GetAttributes(YtdlPath).HasFlag(FileAttributes.ReadOnly))
        {
            Log.Warning("Skipping yt-dlp download because location is unwritable.");
            return;
        }

        string assetName;
        if (OperatingSystem.IsWindows())
            assetName = "yt-dlp.exe";
        else if (OperatingSystem.IsLinux())
            assetName = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "yt-dlp_linux",
                Architecture.Arm64 => "yt-dlp_linux_aarch64",
                _ => throw new($"Unsupported architecture {RuntimeInformation.OSArchitecture}")
            };
        else
            throw new($"Unsupported operating system {Environment.OSVersion}");

        foreach (var assetVersion in json.assets.Where(assetVersion => assetVersion.name == assetName))
        {
            if (assetVersion.name != assetName)
                continue;

            if (string.IsNullOrEmpty(Program.UtilsPath))
                throw new("Failed to get YT-DLP path");

            // Ensure directory exists
            var ytdlDir = Path.GetDirectoryName(YtdlPath);
            if (!string.IsNullOrEmpty(ytdlDir))
                Directory.CreateDirectory(ytdlDir);

            using var activity = StatusService.Begin(StatusCategory.Provisioning,
                string.Format(Localizer.Get("StatusDownloading"), "yt-dlp"), key: ToolVerifier.YtDlpKey);

            var report = activity.Report;

            async Task DownloadAsync()
            {
                using var response = await HttpClient.GetAsync(assetVersion.browser_download_url,
                    HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var stream = new ProgressStream(
                    await response.Content.ReadAsStreamAsync(), response.Content.Headers.ContentLength,
                    report, DownloadStallTimeout);
                await using var fileStream = new FileStream(YtdlPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
            }

            try
            {
                await RetryAsync(DownloadAsync, "yt-dlp download");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "yt-dlp download failed after retries.");
                return;
            }

            Log.Information("Downloaded YT-DLP.");
            FileTools.MarkFileExecutable(YtdlPath);
            // Must match what TryDownloadYtdlp compares against, or every check re-downloads.
            Versions.CurrentVersion.Ytdlp = string.IsNullOrEmpty(json.name) ? json.tag_name : json.name;
            Versions.Save();
            return;
        }

        throw new("Failed to download YT-DLP");
    }

    private static async Task<bool> CheckIfProcessStarts(string path, string arg = "--version")
    {
        var processName = Path.GetFileNameWithoutExtension(path);
        try
        {
            var process = new Process
            {
                StartInfo = new()
                {
                    FileName = path,
                    Arguments = arg,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                Log.Error("Error starting {ProcessName}: {Output} {Error}", processName, output, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exception while starting {ProcessName}: {Message}", processName, ex.Message);
            return false;
        }

        return true;
    }
}