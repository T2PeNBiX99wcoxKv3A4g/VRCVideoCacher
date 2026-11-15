using System.Collections.Concurrent;
using JetBrains.Annotations;
using VRCVideoCacher.Database;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher;

public enum CacheChangeType
{
    Added,
    Removed,
    Cleared
}
[Singleton]
public partial class CacheManager
{
    private readonly ConcurrentDictionary<string, VideoCache> _cachedAssets = new();
    [PublicAPI] public readonly string CachePath;

    private CacheManager()
    {
        if (string.IsNullOrEmpty(ConfigManager.Instance.Config.CachedAssetPath))
            CachePath = Program.DefaultCachePath;
        else if (Path.IsPathRooted(ConfigManager.Instance.Config.CachedAssetPath))
            CachePath = ConfigManager.Instance.Config.CachedAssetPath;
        else
            CachePath = Path.Join(Program.CurrentProcessPath, ConfigManager.Instance.Config.CachedAssetPath);

        Log.Debug("Using cache path {CachePath}", CachePath);
        BuildCache();
        _ = TryFlushCache();
    }

    // Events for UI
    public static event Action<string, CacheChangeType>? OnCacheChanged;

    private void BuildCache()
    {
        _cachedAssets.Clear();
        Directory.CreateDirectory(CachePath);
        var files = Directory.GetFiles(CachePath);
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            AddToCache(file);
        }
    }

    [PublicAPI]
    public async Task TryFlushCache()
    {
        if (ConfigManager.Instance.Config.CacheMaxSizeInGb <= 0f)
            return;

        var maxCacheSize = (long)(ConfigManager.Instance.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var recentPlayHistory = await DatabaseManager.GetPlayHistoryAsync();
        var oldestFiles = _cachedAssets.OrderBy(x => x.Value.LastModified).ToList();
        while (cacheSize >= maxCacheSize && oldestFiles.Count > 0)
        {
            var oldestFile = oldestFiles.First();
            var filePath = Path.Join(CachePath, oldestFile.Value.FileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                cacheSize -= oldestFile.Value.Size;

                // delete thumbnail if not in recent history
                var videoId = Path.GetFileNameWithoutExtension(oldestFile.Value.FileName);
                if (recentPlayHistory.All(h => h.Id != videoId))
                {
                    var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                    if (File.Exists(thumbnailPath))
                        File.Delete(thumbnailPath);
                }
            }

            _cachedAssets.TryRemove(oldestFile.Key, out _);
            oldestFiles.RemoveAt(0);
        }
    }

    [PublicAPI]
    public void AddToCache(string fileName)
    {
        var filePath = Path.Join(CachePath, fileName);
        if (!File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var videoCache = new VideoCache
        {
            FileName = fileName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };

        var existingCache = _cachedAssets.GetOrAdd(videoCache.FileName, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;

        OnCacheChanged?.Invoke(fileName, CacheChangeType.Added);
        _ = TryFlushCache();
    }

    private long GetCacheSize()
    {
        return _cachedAssets.Sum(cache => cache.Value.Size);
    }

    // Public accessors for UI
    [PublicAPI]
    public IReadOnlyDictionary<string, VideoCache> GetCachedAssets()
        => _cachedAssets.ToDictionary(k => k.Key, v => v.Value);

    [PublicAPI]
    public long GetTotalCacheSize() => GetCacheSize();

    [PublicAPI]
    public int GetCachedVideoCount() => _cachedAssets.Count;

    [PublicAPI]
    public void DeleteCacheItem(string fileName)
    {
        var filePath = Path.Join(CachePath, fileName);
        if (!File.Exists(filePath))
            return;

        File.Delete(filePath);
        _cachedAssets.TryRemove(fileName, out _);
        OnCacheChanged?.Invoke(fileName, CacheChangeType.Removed);
        Log.Information("Deleted cached video: {FileName}", fileName);
    }

    [PublicAPI]
    public async Task ClearCache()
    {
        var recentPlayHistory = await DatabaseManager.GetPlayHistoryAsync();
        var files = _cachedAssets.Keys.ToList();
        foreach (var fileName in files)
        {
            var filePath = Path.Join(CachePath, fileName);
            if (!File.Exists(filePath))
                continue;

            Try.Run(() =>
            {
                File.Delete(filePath);

                // delete thumbnail if not in recent history
                var videoId = Path.GetFileNameWithoutExtension(fileName);
                if (recentPlayHistory.Any(h => h.Id == videoId)) return;
                var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                if (File.Exists(thumbnailPath))
                    File.Delete(thumbnailPath);
            }).OnFailure(ex => Log.Error(ex, "Failed to delete {FileName}: {Error}", fileName, ex.ToString()));
        }

        _cachedAssets.Clear();
        OnCacheChanged?.Invoke(string.Empty, CacheChangeType.Cleared);
        Log.Information("Cache cleared");
    }
}