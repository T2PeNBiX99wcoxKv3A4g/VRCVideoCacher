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

        if (prefetchRequest.Method == HttpMethod.Get &&
            prefetchResponse.Content.Headers.ContentType?.MediaType == "application/vnd.apple.mpegurl")
        {
            var body = await prefetchResponse.Content.ReadAsStringAsync();
            var firstM3U8Url = body.Split('\n')
                .FirstOrDefault(line => Uri.IsWellFormedUriString(line, UriKind.RelativeOrAbsolute));
            if (firstM3U8Url != null)
            {
                targetUrl = firstM3U8Url;
                targetName = "M3U8 stream";
                statusCode = await GetHeadStatusCodeAsync(targetUrl);
            }
        }

        if (statusCode < 400)
        {
            Log.Information("Prefetching {Target} returned status code {Status}, proceeding.", targetName, statusCode);
            return true;
        }

        return await RetryUntilSuccessAsync(targetUrl, targetName, statusCode, maxRetryCount);
    }

    private async Task<bool> RetryUntilSuccessAsync(string targetUrl, string targetName, int statusCode,
        int maxRetryCount)
    {
        const int wait = 1500;
        for (var attempt = 1; statusCode >= 400 && attempt < maxRetryCount; attempt++)
        {
            Log.Warning(
                "Prefetching {Target} returned status code {Status}, retrying... ({Attempt}/{Limit})",
                targetName, statusCode, attempt + 1, maxRetryCount);
            await Task.Delay(wait);
            statusCode = await GetHeadStatusCodeAsync(targetUrl);
        }

        if (statusCode >= 400)
        {
            Log.Error(
                "Prefetching {Target} failed after {Limit} attempts, status code {Status}. Video may not play.",
                targetName, maxRetryCount, statusCode);
            return false;
        }

        Log.Information("Prefetching {Target} returned status code {Status}, proceeding.", targetName, statusCode);
        return true;
    }

    private async Task<int> GetHeadStatusCodeAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await _httpClient.SendAsync(request);
        return (int)response.StatusCode;
    }
}
