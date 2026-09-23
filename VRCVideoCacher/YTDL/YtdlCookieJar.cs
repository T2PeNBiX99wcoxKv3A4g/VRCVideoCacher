namespace VRCVideoCacher.YTDL;

/// <summary>
/// Serialises yt-dlp metadata extractions and link resolutions that pass <c>--cookies</c>.
///
/// yt-dlp does not merely READ the cookie jar — on exit it writes the whole file back, because YouTube
/// rotates session tokens (<c>__Secure-1PSIDTS</c>, <c>SIDCC</c>, …) on every request and yt-dlp
/// persists the refreshed ones. Two yt-dlp processes sharing the file therefore each load the jar, each
/// receive a DIFFERENT rotated token, and each rewrite the whole file: last writer wins and the other
/// rotation is silently lost. The persisted session is then inconsistent, and YouTube answers the next
/// request with <i>"Sign in to confirm you're not a bot"</i>.
///
/// Short queries (e.g. <c>yt-dlp -J</c> in VideoId / SabrExtractor) share and update the central cookie
/// jar, serialised via this gate to preserve updated tokens. Long-running cache downloads in VideoDownloader
/// run with an isolated per-job temporary copy of the cookie jar, avoiding holding this gate and allowing
/// playback and URL resolution requests to proceed immediately without waiting for downloads to finish.
/// </summary>
public static class YtdlCookieJar
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Hold this for the whole lifetime of a yt-dlp metadata/resolution process, from Start until it has exited.</summary>
    public static async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Gate.Release();
        }
    }
}