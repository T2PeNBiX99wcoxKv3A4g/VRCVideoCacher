using System.Text.Json;
using JetBrains.Annotations;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public static class YouTubeMetadataService
{
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    [PublicAPI]
    public static async Task<VideoInfoCache?> GetVideoTitleAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        return await Try.Run(async () =>
        {
            var url = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json";
            var response = await HttpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("title", out var titleElement))
                return null;
            var title = titleElement.GetString();
            doc.RootElement.TryGetProperty("author_name", out var authorElement);
            var author = authorElement.GetString();
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(author))
                return null;

            var videoInfo = new VideoInfoCache
            {
                Id = videoId,
                Title = title,
                Author = author,
                Type = UrlType.YouTube
            };
            await DatabaseManager.AddVideoInfoCacheAsync(videoInfo);
            return videoInfo;
        }).GetOrElse(_ => Task.FromResult<VideoInfoCache?>(null));
    }

    public static async Task<string?> GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = ThumbnailManager.GetThumbnailPath(videoId);
        if (File.Exists(localPath))
            return localPath;

        var url = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
        var thumbnailPath = await ThumbnailManager.TrySaveThumbnail(videoId, url);
        return !string.IsNullOrEmpty(thumbnailPath) ? thumbnailPath : url;
    }

    public static async Task<VideoInfoCache?> GetVideoMetadataAsync(string videoId)
    {
        var cachedInfo = await DatabaseManager.GetVideoInfoCacheAsync(videoId);

        if (videoId.Length == 11 && (cachedInfo == null || string.IsNullOrEmpty(cachedInfo.Title) ||
                                     string.IsNullOrEmpty(cachedInfo.Author)))
            cachedInfo = await GetVideoTitleAsync(videoId);

        return cachedInfo;
    }
}