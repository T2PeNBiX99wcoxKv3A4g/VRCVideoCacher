using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Serilog;
using VRCVideoCacher.Extensions;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Detects and verifies direct live-stream URLs (MPEG-TS / HLS / DASH) so a playable one can be handed
/// to the player unchanged. A URL is only passed through after it actually answers with recognisable
/// media; a dead or unauthorized URL must never be returned as if it were playable.
/// </summary>
internal static class DirectStreamProbe
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(DirectStreamProbe));

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);
    private const int MaxCachedUrls = 64;
    private const int PlaylistPrefixBytes = 512;

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = ProbeTimeout,
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };

    // A world that retries the same URL should not make us probe it every time. Both outcomes are
    // cached briefly; a negative entry only delays recovery by the TTL if the stream comes back.
    private static readonly ConcurrentDictionary<string, (bool Playable, DateTime Expires)> Cache = new();

    public static bool IsDirectStreamUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.AbsolutePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IsPlayableAsync(string url, CancellationToken ct = default)
    {
        if (!IsDirectStreamUrl(url))
            return false;

        if (Cache.TryGetValue(url, out var cached) && cached.Expires > DateTime.UtcNow)
            return cached.Playable;

        var result = await Try.Run(() => ProbeAsync(url, ct));
        var playable = result.GetOrElse(ex =>
        {
            Log.Debug(ex, "Direct stream probe failed for {Url}", url);
            return false;
        });

        Remember(url, playable);
        return playable;
    }

    private static async Task<bool> ProbeAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            return false;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return false;

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var path = new Uri(url).AbsolutePath;

        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            var first = new byte[1];
            return await stream.ReadAsync(first, cts.Token) == 1 && LooksLikeMedia(path, first);
        }

        var isHls = path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        var prefix = await ReadPrefixAsync(stream, PlaylistPrefixBytes, isHls ? "#EXTM3U" : "<MPD", cts.Token);
        return LooksLikeMedia(path, prefix);
    }

    /// <summary>
    /// The decisive check that a 200 response is actually media rather than an HTML login/error page.
    /// Kept separate from the network probe so it can be tested without sockets.
    /// </summary>
    internal static bool LooksLikeMedia(string path, ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty)
            return false;

        // MPEG-TS packets start with the 0x47 sync byte.
        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            return prefix[0] == 0x47;

        var text = Encoding.UTF8.GetString(prefix);
        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            return text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n')
                .StartsWith("#EXTM3U", StringComparison.Ordinal);

        return text.Contains("<MPD", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadPrefixAsync(Stream stream, int maxBytes, string stopMarker,
        CancellationToken ct)
    {
        var buffer = new byte[maxBytes];
        var total = 0;

        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
                break;

            total += read;
            if (Encoding.UTF8.GetString(buffer, 0, total).Contains(stopMarker, StringComparison.Ordinal))
                break;
        }

        return buffer[..total];
    }

    private static void Remember(string url, bool playable)
    {
        var now = DateTime.UtcNow;

        if (Cache.Count >= MaxCachedUrls)
            foreach (var stale in Cache.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                Cache.TryRemove(stale, out _);

        if (Cache.Count >= MaxCachedUrls)
            Cache.Clear();

        Cache[url] = (playable, now + CacheTtl);
    }
}
