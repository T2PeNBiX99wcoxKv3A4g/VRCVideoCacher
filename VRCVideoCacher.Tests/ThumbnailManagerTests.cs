using VRCVideoCacher.Services;

namespace VRCVideoCacher.Tests;

public class ThumbnailManagerTests
{
    // Port 1 refuses immediately, so the test never depends on the network or a slow timeout.
    private const string UnreachableUrl = "http://127.0.0.1:1/thumb.jpg";

    [Fact]
    public async Task FailedDownloadLeavesNoThumbnailAndNoPartialFile()
    {
        var videoId = $"test-{Guid.NewGuid():N}";
        var path = ThumbnailManager.GetThumbnailPath(videoId);

        Assert.Null(await ThumbnailManager.TrySaveThumbnail(videoId, UnreachableUrl));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists($"{path}.part"));
    }

    [Fact]
    public async Task ExistingThumbnailIsNotDownloadedAgain()
    {
        var videoId = $"test-{Guid.NewGuid():N}";
        var path = ThumbnailManager.GetThumbnailPath(videoId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "cached");

        try
        {
            Assert.Null(await ThumbnailManager.TrySaveThumbnail(videoId, UnreachableUrl));
            Assert.Equal("cached", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
