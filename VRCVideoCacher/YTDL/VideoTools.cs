using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL;

public partial class VideoTools : Singleton<VideoTools>
{
    private readonly HttpClient _httpClient = new();

    [PublicAPI]
    public async Task<bool> Prefetch2(string videoUrl, int maxRetryCount = 7)
    {
        // If the URL is invalid, skip prefetching
        if (string.IsNullOrWhiteSpace(videoUrl) || !Uri.IsWellFormedUriString(videoUrl, UriKind.RelativeOrAbsolute))
        {
            Log.Warning("Invalid video URL provided for prefetch: {Url}", videoUrl);
            return false;
        }

        // Determine if the URL is an M3U8 playlist
        var uri = new Uri(videoUrl);
        var isM3U8 = uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                     videoUrl.Contains("mime=application/vnd.apple.mpegurl");

        // Prefetch the video URL
        // - Use GET for M3U8 to extract the direct stream URL
        using var prefetchRequest = new HttpRequestMessage(isM3U8 ? HttpMethod.Get : HttpMethod.Head, videoUrl);
        using var prefetchResponse = await _httpClient.SendAsync(prefetchRequest);
        Log.Information("Video prefetch request returned status code {Status}.", (int)prefetchResponse.StatusCode);

        var targetUrl = videoUrl;
        var targetName = "video";
        var statusCode = (int)prefetchResponse.StatusCode;

        if (prefetchRequest.Method == HttpMethod.Get && prefetchResponse.Content.Headers.ContentType?.MediaType ==
            "application/vnd.apple.mpegurl")
        {
            var body = await prefetchResponse.Content.ReadAsStringAsync();
            var firstM3U8Url = body.Split('\n')
                .FirstOrDefault(line => Uri.IsWellFormedUriString(line, UriKind.RelativeOrAbsolute));
            if (firstM3U8Url != null)
            {
                targetUrl = firstM3U8Url;
                targetName = "M3U8 stream";
                statusCode = await GetHeadStatusCode(targetUrl);
            }
        }

        if (statusCode < 400)
        {
            Log.Information("Prefetching {target} returned status code {status}, proceeding.", targetName, statusCode);
            return true;
        }

        return await RetryUntilSuccess(targetUrl, targetName, statusCode, maxRetryCount);
    }

    private static async Task<bool> RetryUntilSuccess(string targetUrl, string targetName, int statusCode, int maxRetryCount)
    {
        const int wait = 1500;
        for (var attempt = 1; statusCode >= 400 && attempt < maxRetryCount; attempt++)
        {
            using var m3u8Request = new HttpRequestMessage(HttpMethod.Head, firstM3U8Url);
            using var m3u8Response = await _httpClient.SendAsync(m3u8Request);
            statusCode = (int)m3u8Response.StatusCode;

            if (statusCode >= 400)
            {
                Log.Warning(
                    "Prefetching M3U8 stream returned status code {Status}, retrying... ({Attempt}/{Limit})",
                    statusCode, i + 1, maxRetryCount);
                await Task.Delay(wait);
            }
            else
            {
                Log.Information("Prefetching M3U8 stream returned status code {Status}, proceeding.", statusCode);
                break;
            }
            Log.Warning(
                "Prefetching {target} returned status code {status}, retrying... ({attempt}/{limit})",
                targetName, statusCode, attempt + 1, maxRetryCount);
            await Task.Delay(wait);
            statusCode = await GetHeadStatusCode(targetUrl);
        }

        if (statusCode >= 400)
        {
            Log.Error(
                "Prefetching {target} failed after {Limit} attempts, status code {Status}. Video may not play.",
                targetName, maxRetryCount, statusCode);
            return false;
        }

        Log.Information("Prefetching {target} returned status code {status}, proceeding.", targetName, statusCode);
        return true;
    }

    private static async Task<int> GetHeadStatusCode(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await HttpClient.SendAsync(request);
        return (int)response.StatusCode;

        return true;
    }
}