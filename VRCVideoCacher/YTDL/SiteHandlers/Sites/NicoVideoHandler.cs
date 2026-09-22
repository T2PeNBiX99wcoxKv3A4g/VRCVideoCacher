using System.Text.RegularExpressions;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public partial class NicoVideoHandler : Handler<NicoVideoHandler>
{
    public override bool CanHandle(Uri uri) => false; // rewrite only, GenericHandler picks up after

    public override Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);

    public Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!uri.Host.EndsWith("nicovideo.jp") && !uri.Host.EndsWith("nico.ms"))
            return Task.FromResult(url);

        var (m, group) = new[]
        {
            (NicoID1().Match(url), 4), (NicoID2().Match(url), 2), (NicoID4().Match(url), 1)
        }.FirstOrDefault(x => x.Item1.Success);

        if (m?.Success != true)
            return Task.FromResult(url);

        var rawId = m.Groups[group].Value;
        var cleanId = rawId.Split('?')[0].Split('#')[0];
        var newUrl = $"https://www.nicovideo.life/watch?v={cleanId}";
        Log.Information("Incompatible URL, passing to external resolver: {Url}", newUrl);
        return Task.FromResult(newUrl);
    }

    // Matches full nicovideo/niconico URLs
    [GeneratedRegex(@"^(https?)://(live|www)\.nicovideo\.jp/(watch|shorts)/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID1();

    [GeneratedRegex(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID2();

    // Matches bare Nico video/live IDs
    [GeneratedRegex(
        @"^(sm\d+|nm\d+|am\d+|fz\d+|ut\d+|dm\d+|so\d+|ax\d+|ca\d+|cd\d+|cw\d+|fx\d+|ig\d+|na\d+|om\d+|sd\d+|sk\d+|yk\d+|yo\d+|za\d+|zb\d+|zc\d+|zd\d+|ze\d+|nl\d+|ch\d+|\d+|lv\d+|ss\d+)$",
        RegexOptions.Compiled)]
    private static partial Regex NicoID4();
}