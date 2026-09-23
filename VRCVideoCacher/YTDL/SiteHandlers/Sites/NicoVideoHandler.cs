using System.Text.RegularExpressions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public partial class NicoVideoHandler : Handler<NicoVideoHandler>
{
    private static readonly string[] Hosts =
    [
        "nicovideo.jp",
        "www.nicovideo.jp",
        "live.nicovideo.jp",
        "cas.nicovideo.jp",
        "nico.ms"
    ];

    public override bool CanHandle(Uri uri) =>
        Hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var cleanId = NicoVideoApiService.ExtractNicoId(url);
        var videoId = string.IsNullOrEmpty(cleanId) || cleanId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? VideoId.HashUrl(url)
            : cleanId;

        Log.Information("Handling NicoVideo URL: {Url} (ID: {VideoId})", url, videoId);

        _ = Task.Run(async () => await NicoVideoApiService.DownloadMetadata(cleanId, videoId));

        return Task.FromResult<VideoInfo?>(new()
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });
    }

    public override Task<string> RewriteUrl(string url, Uri uri)
    {
        var isNicoHost = uri.Host.EndsWith("nicovideo.jp", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.EndsWith("nico.ms", StringComparison.OrdinalIgnoreCase);

        if (!isNicoHost)
            return Task.FromResult(url);

        var (m, group) = new[]
        {
            (NicoID1().Match(url), 4), (NicoID2().Match(url), 2), (NicoID3().Match(url), 2), (NicoID4().Match(url), 1)
        }.FirstOrDefault(x => x.Item1.Success);

        if (m?.Success != true)
            return Task.FromResult(url);

        var rawId = m.Groups[group].Value;
        var cleanId = rawId.Split('?')[0].Split('#')[0];
        var canonicalUrl = $"https://www.nicovideo.jp/watch/{cleanId}";
        Log.Information("Normalized NicoVideo URL: {NormalizedUrl}", canonicalUrl);
        return Task.FromResult(canonicalUrl);
    }

    // Matches full nicovideo/niconico URLs
    [GeneratedRegex(@"^(https?)://(live|www)\.nicovideo\.jp/(watch|shorts)/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID1();

    [GeneratedRegex(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID2();

    [GeneratedRegex(@"^(https?)://(www\.)?nicovideo\.jp/shorts/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID3();

    // Matches bare Nico video/live IDs
    [GeneratedRegex(
        @"^(sm\d+|nm\d+|am\d+|fz\d+|ut\d+|dm\d+|so\d+|ax\d+|ca\d+|cd\d+|cw\d+|fx\d+|ig\d+|na\d+|om\d+|sd\d+|sk\d+|yk\d+|yo\d+|za\d+|zb\d+|zc\d+|zd\d+|ze\d+|nl\d+|ch\d+|\d+|lv\d+|ss\d+)$",
        RegexOptions.Compiled)]
    private static partial Regex NicoID4();
}