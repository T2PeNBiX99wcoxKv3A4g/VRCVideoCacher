using System.Collections.Concurrent;
using JetBrains.Annotations;
using VRCVideoCacher.Database;
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

public partial class CacheManager : Singleton<CacheManager>
{
    private readonly ConcurrentDictionary<string, VideoCache> _cachedAssets = new();
    [PublicAPI]
    public readonly string CachePath2;

    public CacheManager()
    {
        if (string.IsNullOrEmpty(ConfigManager.Config.CachedAssetPath))
            CachePath2 = Path.Join(GetSystemCacheFolder(), "CachedAssets");
        else if (Path.IsPathRooted(ConfigManager.Config.CachedAssetPath))
            CachePath2 = ConfigManager.Config.CachedAssetPath;
        else
            CachePath2 = Path.Join(Program.CurrentProcessPath, ConfigManager.Config.CachedAssetPath);

        Log.Debug("Using cache path {CachePath}", CachePath);
        BuildCache();
        TryFlushCache();
    }

    // Events for UI
    public static event Action<string, CacheChangeType>? OnCacheChanged;

    private static string GetSystemCacheFolder()
    {
        if (OperatingSystem.IsWindows())
            return Program.DataPath;

        var cachePath = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cachePath))
            cachePath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

        return Path.Join(cachePath, "VRCVideoCacher");
    }

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
    public void TryFlushCache2()
    {
        if (ConfigManager.Config.CacheMaxSizeInGb <= 0f)
            return;

        var maxCacheSize = (long)(ConfigManager.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var recentPlayHistory = DatabaseManager.GetPlayHistory();
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
    public void AddToCache2(string fileName)
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
        TryFlushCache();
    }

    private long GetCacheSize()
    {
        return _cachedAssets.Sum(cache => cache.Value.Size);
    }

    // Public accessors for UI
    [PublicAPI]
    public IReadOnlyDictionary<string, VideoCache> GetCachedAssets2()
        => _cachedAssets.ToDictionary(k => k.Key, v => v.Value);

    [PublicAPI]
    public long GetTotalCacheSize2() => GetCacheSize();

    [PublicAPI]
    public int GetCachedVideoCount2() => _cachedAssets.Count;

    [PublicAPI]
    public void DeleteCacheItem2(string fileName)
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
    public void ClearCache2()
    {
        var recentPlayHistory = DatabaseManager.GetPlayHistory();
        var files = _cachedAssets.Keys.ToList();
        foreach (var fileName in files)
        {
            var filePath = Path.Join(CachePath, fileName);
            if (!File.Exists(filePath))
                continue;

            try
            {
                File.Delete(filePath);

                // delete thumbnail if not in recent history
                var videoId = Path.GetFileNameWithoutExtension(fileName);
                if (recentPlayHistory.All(h => h.Id != videoId))
                {
                    var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                    if (File.Exists(thumbnailPath))
                        File.Delete(thumbnailPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete {FileName}: {Error}", fileName, ex.ToString());
            }
        }

        _cachedAssets.Clear();
        OnCacheChanged?.Invoke(string.Empty, CacheChangeType.Cleared);
        Log.Information("Cache cleared");
    }
}