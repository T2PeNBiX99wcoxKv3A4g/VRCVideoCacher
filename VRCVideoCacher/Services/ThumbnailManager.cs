using Serilog;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public static class ThumbnailManager
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(ThumbnailManager));
    public static readonly string CacheDir = Path.Join(Program.DataPath, "MetadataCache");
    public static readonly string ThumbnailCacheDir = Path.Join(CacheDir, "thumbnails");

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    static ThumbnailManager()
    {
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ThumbnailCacheDir);
    }

    public static string GetThumbnailPath(string videoId) => Path.Join(ThumbnailCacheDir, $"{videoId}.jpg");

    public static string? GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = GetThumbnailPath(videoId);
        return File.Exists(localPath) ? localPath : null;
    }

    public static async Task<string?> TrySaveThumbnail(string videoId, string url, CancellationToken ct = default)
    {
        var thumbnailPath = GetThumbnailPath(videoId);
        if (File.Exists(thumbnailPath))
            return null;

        // A half-written JPEG would otherwise be treated as a valid cache entry forever.
        var tempPath = $"{thumbnailPath}.part";

        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var data = await response.Content.ReadAsStreamAsync(ct);
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await data.CopyToAsync(fileStream, ct);

            File.Move(tempPath, thumbnailPath, true);
            return thumbnailPath;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to download thumbnail for {VideoId}", videoId);
            return null;
        }
        finally
        {
            if (File.Exists(tempPath))
                Try.Run(() => File.Delete(tempPath));
        }
    }
}