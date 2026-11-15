using VRCVideoCacher.YTDL.SiteHandlers;
using VRCVideoCacher.YTDL.SiteHandlers.Sites;

namespace VRCVideoCacher.Tests;

public class SiteHandlerTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", typeof(YouTubeHandler))]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", typeof(YouTubeHandler))]
    [InlineData("https://api.pypy.dance/video/abc", typeof(PyPyDanceHandler))]
    [InlineData("https://na2.vrdancing.club/world/123", typeof(VRDancingHandler))]
    [InlineData("https://x.com/someone/status/123", typeof(TwitterHandler))]
    [InlineData("https://twitter.com/someone/status/123", typeof(TwitterHandler))]
    [InlineData("https://www.nicovideo.jp/watch/sm9", typeof(NicoVideoHandler))]
    [InlineData("https://nico.ms/sm9", typeof(NicoVideoHandler))]
    [InlineData("https://example.com/video.mp4", typeof(GenericHandler))]
    public void ResolvesTheSiteHandlerForTheHost(string url, Type expected) =>
        Assert.IsType(expected, SiteHandlerRegistry.Resolve(new(url)));

    [Fact]
    public void UnknownHostFallsBackToGenericWithoutClaimingSupport()
    {
        var uri = new Uri("https://example.com/video.mp4");

        Assert.False(SiteHandlerRegistry.HasSpecificHandler(uri));
        Assert.IsType<GenericHandler>(SiteHandlerRegistry.Resolve(uri));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    public async Task KnownHostsAreLeftAloneByTheRewriters(string url) =>
        Assert.Equal(url, await SiteHandlerRegistry.ApplyRewrites(url));

    [Fact]
    public async Task KaraokeInSyncCustomUrlIsUnwrapped()
    {
        // The player puts the real link after a literal colon in the query, so the rewrite has to split
        // on that colon and percent-decode the remainder.
        const string url =
            "https://ksync.arcanescripts.com/custom/redir-url?Paste%20the%20YouTube%20Link%20after%20the%20colon:" +
            "https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3Dabc123";

        Assert.Equal("https://www.youtube.com/watch?v=abc123",
            await new KaraokeInSyncHandler().RewriteUrl(url, new Uri(url)));
    }

    [Fact]
    public async Task UnrecognisedKaraokeInSyncUrlIsPassedThrough()
    {
        const string url = "https://ksync.arcanescripts.com/custom/redir-url?id=abc";

        Assert.Equal(url, await new KaraokeInSyncHandler().RewriteUrl(url, new Uri(url)));
    }

    [Fact]
    public async Task YtsPlaceholderIsRewrittenToTheYouTubePath()
    {
        const string url = "https://dmn.moe/sr/dQw4w9WgXcQ";

        Assert.Equal("https://dmn.moe/yt/dQw4w9WgXcQ", await new YTSHandler().RewriteUrl(url, new Uri(url)));
    }
}
