using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Tests;

public class DirectStreamProbeTests
{
    [Theory]
    [InlineData("https://stream.vrchat.vrcdn.live/live/vrchat.live.ts")]
    [InlineData("https://example.com/live/index.m3u8?token=abc")]
    [InlineData("https://example.com/manifest.mpd")]
    [InlineData("https://example.com/UPPER.TS")]
    public void RecognizesDirectStreamUrls(string url) => Assert.True(DirectStreamProbe.IsDirectStreamUrl(url));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://example.com/video.mp4")]
    [InlineData("https://example.com/page.html")]
    [InlineData("not a url")]
    public void IgnoresNonStreamUrls(string url) => Assert.False(DirectStreamProbe.IsDirectStreamUrl(url));

    [Fact]
    public void AcceptsMpegTsSyncByte()
    {
        Assert.True(DirectStreamProbe.LooksLikeMedia("live.ts", [0x47, 0x40, 0x00, 0x10]));
    }

    [Fact]
    public void RejectsHtmlServedAsMpegTs()
    {
        // Some servers answer 200 with a login/error page; the 0x47 check is what stops that from
        // being handed to the player as a playable stream.
        Assert.False(DirectStreamProbe.LooksLikeMedia("live.ts", "<!DOCTYPE html>"u8));
    }

    [Theory]
    [InlineData("index.m3u8", "#EXTM3U\n#EXT-X-VERSION:3\n")]
    [InlineData("manifest.mpd", "<?xml version=\"1.0\"?><MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\">")]
    public void AcceptsHlsAndDash(string path, string prefix) =>
        Assert.True(DirectStreamProbe.LooksLikeMedia(path, System.Text.Encoding.UTF8.GetBytes(prefix)));

    [Theory]
    [InlineData("index.m3u8", "<!DOCTYPE html>")]
    [InlineData("manifest.mpd", "{\"error\":\"unauthorized\"}")]
    public void RejectsHtmlOrJsonPlaylists(string path, string prefix) =>
        Assert.False(DirectStreamProbe.LooksLikeMedia(path, System.Text.Encoding.UTF8.GetBytes(prefix)));
}
