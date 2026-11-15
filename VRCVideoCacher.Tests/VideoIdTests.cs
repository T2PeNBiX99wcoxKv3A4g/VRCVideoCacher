using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Tests;

public class VideoIdTests
{
    [Fact]
    public void HashIsSafeToUseAsAFileName()
    {
        // The hash becomes the cached file name, so it must not contain path separators or any other
        // character that Windows rejects.
        var hash = VideoId.HashUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.DoesNotContain('/', hash);
        Assert.DoesNotContain('+', hash);
        Assert.DoesNotContain('=', hash);
        Assert.All(hash, character => Assert.True(char.IsLetterOrDigit(character), $"'{character}' is not usable"));
        Assert.Equal(hash, VideoId.HashUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
    }

    [Fact]
    public void DifferentUrlsHashDifferently()
    {
        Assert.NotEqual(VideoId.HashUrl("https://example.com/a"), VideoId.HashUrl("https://example.com/b"));
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void ToUriRejectsWhatIsNotAnAbsoluteUrl(string url, bool expected) =>
        Assert.Equal(expected, VideoId.ToUri(url) is not null);
}
