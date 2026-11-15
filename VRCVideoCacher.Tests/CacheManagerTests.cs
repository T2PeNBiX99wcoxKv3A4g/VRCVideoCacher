using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.Tests;

public class CacheManagerTests
{
    // A limit of 1 KB does not fit two 1 KB files, so a flush has to evict until only one is left.
    private const float OneKilobyteLimitGb = 1e-6f;

    [Fact]
    public async Task EvictsTheOldestFilesUntilTheCacheFits()
    {
        var cache = await PrepareCacheAsync();
        var oldest = CreateVideo(cache, "oldest.mp4", DateTime.UtcNow.AddHours(-2));
        var newest = CreateVideo(cache, "newest.mp4", DateTime.UtcNow.AddHours(-1));
        cache.AddToCache("oldest.mp4");
        cache.AddToCache("newest.mp4");

        await FlushWithLimitAsync(cache);

        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(newest));
        Assert.Null(cache.GetCachedAsset("oldest.mp4"));
        Assert.NotNull(cache.GetCachedAsset("newest.mp4"));
    }

    [Fact]
    public async Task EvictsUntilTheLimitIsMetButKeepsTheMostRecentFile()
    {
        var cache = await PrepareCacheAsync();
        CreateVideo(cache, "first.mp4", DateTime.UtcNow.AddHours(-3));
        CreateVideo(cache, "second.mp4", DateTime.UtcNow.AddHours(-2));
        var last = CreateVideo(cache, "third.mp4", DateTime.UtcNow.AddHours(-1));
        cache.AddToCache("first.mp4");
        cache.AddToCache("second.mp4");
        cache.AddToCache("third.mp4");

        await FlushWithLimitAsync(cache);

        Assert.Single(cache.GetCachedAssets());
        Assert.True(File.Exists(last));
    }

    [Fact]
    public async Task EvictedVideoLosesItsThumbnailUnlessItIsStillInThePlayHistory()
    {
        var cache = await PrepareCacheAsync();
        CreateVideo(cache, "unplayed.mp4", DateTime.UtcNow.AddHours(-3));
        CreateVideo(cache, "played.mp4", DateTime.UtcNow.AddHours(-2));
        CreateVideo(cache, "kept.mp4", DateTime.UtcNow.AddHours(-1));
        cache.AddToCache("unplayed.mp4");
        cache.AddToCache("played.mp4");
        cache.AddToCache("kept.mp4");

        var unplayedThumbnail = CreateThumbnail("unplayed");
        var playedThumbnail = CreateThumbnail("played");
        await DatabaseManager.AddPlayHistoryAsync(new VideoInfo
        {
            VideoId = "played",
            VideoUrl = "https://example.com/played.mp4",
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });

        await FlushWithLimitAsync(cache);

        Assert.False(File.Exists(unplayedThumbnail));
        Assert.True(File.Exists(playedThumbnail));
    }

    [Fact]
    public async Task ZeroLimitKeepsEverything()
    {
        var cache = await PrepareCacheAsync();
        var first = CreateVideo(cache, "unlimited-1.mp4", DateTime.UtcNow.AddHours(-2));
        var second = CreateVideo(cache, "unlimited-2.mp4", DateTime.UtcNow.AddHours(-1));
        cache.AddToCache("unlimited-1.mp4");
        cache.AddToCache("unlimited-2.mp4");

        await cache.TryFlushCache();

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(2, cache.GetCachedVideoCount());
    }

    /// <summary>Empties the cache and holds eviction off while a test arranges its files.</summary>
    private static async Task<CacheManager> PrepareCacheAsync()
    {
        var cache = CacheManager.Instance;
        await cache.ClearCache();

        // AddToCache schedules a flush on its own, so arrange files with eviction disabled and let any
        // flush those calls started finish before the test raises the limit.
        ConfigManager.Instance.Config.CacheMaxSizeInGb = 0f;
        await cache.TryFlushCache();

        return cache;
    }

    private static async Task FlushWithLimitAsync(CacheManager cache)
    {
        ConfigManager.Instance.Config.CacheMaxSizeInGb = OneKilobyteLimitGb;
        await cache.TryFlushCache();
    }

    private static string CreateVideo(CacheManager cache, string fileName, DateTime lastWriteUtc)
    {
        var path = Path.Join(cache.CachePath, fileName);
        Directory.CreateDirectory(cache.CachePath);
        File.WriteAllBytes(path, new byte[1024]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static string CreateThumbnail(string videoId)
    {
        var path = ThumbnailManager.GetThumbnailPath(videoId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "thumbnail");
        return path;
    }
}
