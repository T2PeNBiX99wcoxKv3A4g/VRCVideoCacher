namespace VRCVideoCacher.Utils;

public static class HttpUtil
{
    internal static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        }
    };
}