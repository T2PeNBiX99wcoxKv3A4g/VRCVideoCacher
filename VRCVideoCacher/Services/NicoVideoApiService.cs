using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Memory;
using VRCVideoCacher.Database;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services.Nico;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public class NicoVideoResult
{
    public string? VideoId { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public long? ViewCount { get; set; }
    public long? CommentCount { get; set; }
    public long? MyListCount { get; set; }
    public long? LikeCount { get; set; }
    public long? Duration { get; set; }
    public string? Thumbnail { get; set; }
    public string? StreamUrl { get; set; }
    public Dictionary<string, string> Cookies { get; set; } = new();
}

public partial class NicoVideoApiService : Singleton<NicoVideoApiService>
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public NicoVideoApiService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = false
        };
        _httpClient = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    [GeneratedRegex(@"<meta name=""server-response"" content=""\{(.+)\}"" />", RegexOptions.Compiled)]
    private static partial Regex ServerResponseMetaRegex();

    [GeneratedRegex(@"<script id=""embedded-data"" data-props=""(.+?)""></script><script id=""", RegexOptions.Compiled)]
    private static partial Regex EmbeddedDataScriptRegex();

    [PublicAPI]
    public async Task<NicoVideoResult?> FetchVideoResult2(string videoIdOrUrl)
    {
        var cleanId = ExtractNicoId(videoIdOrUrl);
        if (_cache.TryGetValue(cleanId, out NicoVideoResult? cached) && cached != null &&
            !string.IsNullOrEmpty(cached.Title) && !string.IsNullOrEmpty(cached.Author)) return cached;

        return await Try.Run<NicoVideoResult?>(async () =>
        {
            var watchUrl = cleanId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? cleanId
                : $"https://www.nicovideo.jp/watch/{cleanId}";

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, watchUrl);
            getRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            getRequest.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            getRequest.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");

            using var getResponse = await _httpClient.SendAsync(getRequest);
            if (!getResponse.IsSuccessStatusCode)
            {
                Log.Warning("NicoVideo web page request failed with status code {StatusCode} for {Url}",
                    getResponse.StatusCode, watchUrl);
                return null;
            }

            var html = await getResponse.Content.ReadAsStringAsync();
            var cookieList = getResponse.Headers.TryGetValues("Set-Cookie", out var setCookies)
                ? setCookies.ToList()
                : [];

            var cookieMap = new Dictionary<string, string>();
            foreach (var parts in cookieList.Select(sc => sc.Split(';')[0]).Select(first => first.Split('=', 2))
                         .Where(parts => parts.Length == 2))
                cookieMap[parts[0].Trim()] = parts[1].Trim();

            // Extract JSON embedded in HTML
            NicoWatchPageData? pageData = null;
            var metaMatch = ServerResponseMetaRegex().Match(html);
            if (metaMatch.Success)
            {
                var jsonStr = "{" + WebUtility.HtmlDecode(metaMatch.Groups[1].Value) + "}";
                pageData = JsonSerializer.Deserialize(jsonStr, NicoJsonContext.Default.NicoWatchPageData);
            }
            else
            {
                var scriptMatch = EmbeddedDataScriptRegex().Match(html);
                if (scriptMatch.Success)
                {
                    var jsonStr = WebUtility.HtmlDecode(scriptMatch.Groups[1].Value);
                    pageData = JsonSerializer.Deserialize(jsonStr, NicoJsonContext.Default.NicoWatchPageData);
                }
            }

            if (pageData == null)
            {
                Log.Warning("Could not parse embedded NicoVideo JSON metadata for {Url}", watchUrl);
                return null;
            }

            var result = new NicoVideoResult
            {
                VideoId = cleanId,
                Url = watchUrl
            };

            var responseObj = pageData.Data?.Response;

            if (responseObj != null)
            {
                // Normal video
                var videoNode = responseObj.Video;
                if (videoNode != null)
                {
                    result.Title = videoNode.Title;
                    result.Description = videoNode.Description;
                    result.Duration = videoNode.Duration;
                    result.Thumbnail = videoNode.Thumbnail?.Player ?? videoNode.Thumbnail?.Url;

                    if (videoNode.Count != null)
                    {
                        result.ViewCount = videoNode.Count.View;
                        result.CommentCount = videoNode.Count.Comment;
                        result.MyListCount = videoNode.Count.MyList;
                        result.LikeCount = videoNode.Count.Like;
                    }
                }

                if (responseObj.Owner != null)
                    result.Author = responseObj.Owner.Nickname;

                if (responseObj.Tag?.Items != null)
                    result.Tags =
                    [
                        .. responseObj.Tag.Items
                            .Select(t => t.Name)
                            .Where(name => !string.IsNullOrEmpty(name))
                            .Select(name => name!)
                    ];

                // Domand media access rights (NVAPI)
                var domandNode = responseObj.Media?.Domand;
                var clientNode = responseObj.Client;
                var accessRightKey = domandNode?.AccessRightKey;
                var trackId = clientNode?.WatchTrackId;
                var nicosid = clientNode?.NicosId;

                if (!string.IsNullOrEmpty(nicosid))
                    cookieMap["nicosid"] = nicosid;

                if (!string.IsNullOrEmpty(accessRightKey) && !string.IsNullOrEmpty(trackId) && domandNode != null)
                    try
                    {
                        var audioList = new List<string>();
                        if (domandNode.Audios != null)
                            audioList.AddRange(from item in domandNode.Audios
                                where item.IsAvailable == true && !string.IsNullOrEmpty(item.Id)
                                select item.Id);

                        var videoList = new List<string>();
                        if (domandNode.Videos != null)
                            videoList.AddRange(from item in domandNode.Videos
                                where item.IsAvailable == true && !string.IsNullOrEmpty(item.Id)
                                select item.Id);

                        var outputs = new List<string[]>();
                        var firstAudio = audioList.FirstOrDefault();
                        var secondAudio = audioList.Skip(1).FirstOrDefault();

                        foreach (var vid in videoList)
                        {
                            if (firstAudio != null)
                                outputs.Add([vid, firstAudio]);
                            if (secondAudio != null)
                                outputs.Add([vid, secondAudio]);
                        }

                        if (outputs.Count > 0)
                        {
                            var postPayload = JsonSerializer.Serialize(new()
                            {
                                Outputs = outputs
                            }, NicoJsonContext.Default.NicoNvApiAccessRightsRequest);
                            var nvApiUrl =
                                $"https://nvapi.nicovideo.jp/v1/watch/{cleanId}/access-rights/hls?actionTrackId={trackId}";
                            using var postRequest = new HttpRequestMessage(HttpMethod.Post, nvApiUrl);
                            postRequest.Headers.TryAddWithoutValidation("Accept",
                                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                            postRequest.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");
                            postRequest.Headers.TryAddWithoutValidation("X-Access-Right-Key", accessRightKey);
                            postRequest.Headers.TryAddWithoutValidation("X-Frontend-Id", "6");
                            postRequest.Headers.TryAddWithoutValidation("X-Frontend-Version", "0");
                            postRequest.Headers.TryAddWithoutValidation("X-Niconico-Language", "ja-jp");
                            postRequest.Headers.TryAddWithoutValidation("X-Request-With", "nicovideo");
                            postRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                            var cookieHeader = string.Join("; ", cookieMap.Select(kv => $"{kv.Key}={kv.Value}"));
                            if (!string.IsNullOrEmpty(cookieHeader))
                                postRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                            postRequest.Content = new StringContent(postPayload, Encoding.UTF8, "application/json");

                            using var postResponse = await _httpClient.SendAsync(postRequest);
                            if (postResponse.IsSuccessStatusCode)
                            {
                                if (postResponse.Headers.TryGetValues("Set-Cookie", out var nvCookies))
                                    foreach (var sc in nvCookies)
                                    {
                                        var first = sc.Split(';')[0];
                                        var parts = first.Split('=', 2);
                                        if (parts.Length == 2)
                                            cookieMap[parts[0].Trim()] = parts[1].Trim();
                                    }

                                var postJsonStr = await postResponse.Content.ReadAsStringAsync();
                                var nvApiResponse = JsonSerializer.Deserialize(postJsonStr,
                                    NicoJsonContext.Default.NicoNvApiAccessRightsResponse);
                                var contentUrl = nvApiResponse?.Data?.ContentUrl;
                                if (!string.IsNullOrEmpty(contentUrl))
                                    result.StreamUrl = contentUrl;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to resolve NVAPI HLS access right for {Id}", cleanId);
                    }
            }
            else if (pageData.Program != null)
            {
                // Nico Live program
                var prog = pageData.Program;
                result.Title = prog.Title;
                result.Description = prog.Description;
                if (prog.Statistics != null)
                {
                    result.ViewCount = prog.Statistics.WatchCount;
                    result.CommentCount = prog.Statistics.CommentCount;
                }
            }

            result.Cookies = cookieMap;
            _cache.Set(cleanId, result, TimeSpan.FromMinutes(5));
            return result;
        }).GetOrElse(ex =>
        {
            Log.Error(ex, "Exception fetching NicoVideo result for {Target}", videoIdOrUrl);
            return Task.FromResult<NicoVideoResult?>(null);
        });
    }

    [PublicAPI]
    public async Task DownloadMetadata2(string cleanId, string videoId)
    {
        await Try.Run(async () =>
        {
            var res = await FetchVideoResult2(cleanId);
            if (res == null) return;

            if (!string.IsNullOrEmpty(res.Thumbnail))
                await ThumbnailManager.TrySaveThumbnail(videoId, res.Thumbnail);

            await DatabaseManager.AddVideoInfoCacheAsync(new()
            {
                Id = videoId,
                Title = res.Title,
                Author = res.Author,
                Duration = res.Duration != null ? (int?)res.Duration.Value : null,
                Type = UrlType.NicoVideo
            });
        }).OnFailure(ex =>
        {
            Log.Error(ex, "Failed to download NicoVideo metadata: {Ex}", ex.ToString());
            return Unit.TaskValue;
        });
    }

    [PublicAPI]
    public string ExtractNicoId2(string input)
    {
        var url = input.Trim().Split('?')[0].Split('#')[0];
        var m1 = NicoUrlRegex1().Match(url);
        if (m1.Success) return m1.Groups[4].Value;

        var m2 = NicoUrlRegex2().Match(url);
        if (m2.Success) return m2.Groups[2].Value;

        var m3 = NicoShortUrlRegex().Match(url);
        if (m3.Success) return m3.Groups[2].Value;

        var m4 = NicoBareIdRegex().Match(url);
        if (m4.Success) return m4.Groups[1].Value;

        return input;
    }

    [GeneratedRegex(@"^(https?)://(live|www)\.nicovideo\.jp/(watch|shorts)/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoUrlRegex1();

    [GeneratedRegex(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoUrlRegex2();

    [GeneratedRegex(@"^(https?)://(www\.)?nicovideo\.jp/shorts/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoShortUrlRegex();

    [GeneratedRegex(
        @"^(sm\d+|nm\d+|am\d+|fz\d+|ut\d+|dm\d+|so\d+|ax\d+|ca\d+|cd\d+|cw\d+|fx\d+|ig\d+|na\d+|om\d+|sd\d+|sk\d+|yk\d+|yo\d+|za\d+|zb\d+|zc\d+|zd\d+|ze\d+|nl\d+|ch\d+|\d+|lv\d+|ss\d+)$",
        RegexOptions.Compiled)]
    private static partial Regex NicoBareIdRegex();
}