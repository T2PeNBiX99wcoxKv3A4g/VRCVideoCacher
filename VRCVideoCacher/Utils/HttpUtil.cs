using System.Net;

namespace VRCVideoCacher.Utils;

public static class HttpUtil
{
    /// <summary>Short-lived API and metadata calls.</summary>
    internal static readonly HttpClient HttpClient = Create(TimeSpan.FromSeconds(30));

    /// <summary>Large tool/video downloads; stalls are handled by the caller's progress stream.</summary>
    internal static readonly HttpClient DownloadClient = Create(TimeSpan.FromMinutes(30));

    /// <summary>Small, strictly bounded probes such as direct-stream verification.</summary>
    internal static readonly HttpClient ProbeClient = Create(TimeSpan.FromSeconds(5));

    private static HttpClient Create(TimeSpan timeout) => new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = timeout,
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        }
    };
}
