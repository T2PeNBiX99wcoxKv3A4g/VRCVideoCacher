using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Database;

public static class DatabaseManager
{
    public static event Action? OnPlayHistoryAdded;
    public static event Action? OnPlayHistoryChanged;
    public static event Action? OnVideoInfoCacheUpdated;

    private static readonly PooledDbContextFactory<Database> ContextFactory;
    private static readonly SemaphoreSlim DbWriteLock = new(1, 1);

    static DatabaseManager()
    {
        Directory.CreateDirectory(Database.CacheDir);

        var options = new DbContextOptionsBuilder<Database>()
            .UseSqlite($"Data Source={Database.DbPath}")
            .EnableSensitiveDataLogging()
            .Options;

        ContextFactory = new(options);

        using var db = ContextFactory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    public static async Task AddPlayHistoryAsync(VideoInfo videoInfo, CancellationToken cancellationToken = default)
    {
        var history = new History
        {
            Timestamp = DateTime.UtcNow,
            Url = videoInfo.VideoUrl,
            Id = videoInfo.VideoId,
            Type = videoInfo.UrlType
        };

        await DbWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using (UsingUntil.Run(() => DbWriteLock.Release()))
        {
            await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await db.PlayHistory.AddAsync(history, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await TrimPlayHistoryInternalAsync(ConfigManager.Config.HistoryMaxSize, db, cancellationToken)
                .ConfigureAwait(false);
        }

        OnPlayHistoryAdded?.Invoke();
    }

    /// <summary>
    /// Removes a video from history entirely — every play record for it. Identified by video Id when it
    /// has one; otherwise (an entry with no parseable Id) by exact Url, so unrelated Id-less entries are
    /// left alone rather than all deleted together.
    /// </summary>
    public static async Task DeletePlayHistoryForVideoAsync(string? id, string url,
        CancellationToken cancellationToken = default)
    {
        await DbWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using (UsingUntil.Run(() => DbWriteLock.Release()))
        {
            await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(id))
                await db.PlayHistory.Where(h => h.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            else
                await db.PlayHistory.Where(h => h.Url == url).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        OnPlayHistoryChanged?.Invoke();
    }

    /// <summary>Deletes every play record.</summary>
    public static async Task ClearPlayHistoryAsync(CancellationToken cancellationToken = default)
    {
        await DbWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using (UsingUntil.Run(() => DbWriteLock.Release()))
        {
            await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await db.PlayHistory.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        OnPlayHistoryChanged?.Invoke();
    }

    /// <summary>
    /// Enforces the retention cap: keeps the newest <paramref name="max"/> play records and deletes the
    /// rest. Called after each insert (silently) and when the History max-size setting is lowered.
    /// </summary>
    public static async Task TrimPlayHistoryAsync(int max, CancellationToken cancellationToken = default)
    {
        if (max <= 0)
            return;

        await DbWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using (UsingUntil.Run(() => DbWriteLock.Release()))
        {
            await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await TrimPlayHistoryInternalAsync(max, db, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task TrimPlayHistoryInternalAsync(int max, Database db,
        CancellationToken cancellationToken = default)
    {
        if (max <= 0) return;

        // Find the Timestamp of the Nth-newest row; anything strictly older is deleted. Delete by that
        // boundary rather than materialising ids, so it stays one round-trip regardless of table size.
        var cutoff = await db.PlayHistory
            .OrderByDescending(h => h.Timestamp)
            .Skip(max)
            .Select(h => (DateTime?)h.Timestamp)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (cutoff is null)
            return; // fewer than max rows; nothing to trim

        await db.PlayHistory.Where(h => h.Timestamp <= cutoff.Value).ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task AddVideoInfoCacheAsync(VideoInfoCache videoInfoCache,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(videoInfoCache.Id)) return;

        await DbWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using (UsingUntil.Run(() => DbWriteLock.Release()))
        {
            await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var existingCache = await db.VideoInfoCache.FindAsync([videoInfoCache.Id], cancellationToken)
                .ConfigureAwait(false);
            if (existingCache != null)
            {
                if (string.IsNullOrEmpty(existingCache.Title) &&
                    !string.IsNullOrEmpty(videoInfoCache.Title))
                    existingCache.Title = videoInfoCache.Title;

                if (string.IsNullOrEmpty(existingCache.Author) &&
                    !string.IsNullOrEmpty(videoInfoCache.Author))
                    existingCache.Author = videoInfoCache.Author;

                if (existingCache.Duration == null &&
                    videoInfoCache.Duration != null)
                    existingCache.Duration = videoInfoCache.Duration;
            }
            else
                await db.VideoInfoCache.AddAsync(videoInfoCache, cancellationToken).ConfigureAwait(false);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        OnVideoInfoCacheUpdated?.Invoke();
    }

    public static async Task<List<History>> GetPlayHistoryAsync(int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<List<HistoryItemViewModel>> GetVideoHistoryAsCacheAsync(int limit = 50,
        bool distinctOnly = false, CancellationToken cancellationToken = default)
    {
        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<History> histories;

        if (distinctOnly)
            histories = await db.PlayHistory
                .FromSqlRaw($@"
                SELECT ph.* FROM {nameof(Database.PlayHistory)} ph
                INNER JOIN (
                    SELECT {nameof(History.Id)}, MAX({nameof(History.Timestamp)}) as MaxTimestamp
                    FROM {nameof(Database.PlayHistory)}
                    GROUP BY {nameof(History.Id)}
                ) latest ON ph.{nameof(History.Id)} = latest.{nameof(History.Id)} AND ph.{nameof(History.Timestamp)} = latest.MaxTimestamp
                ORDER BY ph.{nameof(History.Timestamp)} DESC
                LIMIT {{0}}", limit)
                .AsNoTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        else
            histories = await db.PlayHistory
                .AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Fetch matching VideoInfoCache entries
        var ids = histories.Select(h => h.Id).Where(id => id != null).Distinct().ToList();
        var cacheDict = await db.VideoInfoCache
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken).ConfigureAwait(false);

        // Project to ViewModel in-memory
        return
        [
            .. histories.Select(h =>
            {
                cacheDict.TryGetValue(h.Id ?? string.Empty, out var meta);
                return new HistoryItemViewModel(h, meta);
            })
        ];
    }

    public static async Task<VideoInfoCache?> GetVideoInfoCacheAsync(string videoId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.VideoInfoCache.FindAsync([videoId], cancellationToken).ConfigureAwait(false);
    }
}