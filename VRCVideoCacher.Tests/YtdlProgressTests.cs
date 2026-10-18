using VRCVideoCacher.Services;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Tests;

public class YtdlProgressTests
{
    private static StatusActivity NewActivity() =>
        StatusService.Begin(StatusCategory.Downloading, "test");

    [Fact]
    public void SingleStream_ReportsPercent()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        progress.Feed("[download] 42.0% of 10.00MiB at 1.00MiB/s ETA 00:05");

        Assert.Equal(0.42, activity.Progress!.Value, 3);
    }

    [Fact]
    public void ConcurrentStreams_AreWeightedByBytes()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        // Both streams report before either makes progress, exactly like yt-dlp's concurrent output.
        progress.Feed("1: [download]   0.0% of 4.00MiB at 0.00B/s ETA Unknown (140)");
        progress.Feed("2: [download]   0.0% of 8.00MiB at 0.00B/s ETA Unknown (134)");

        // audio 4 MiB half done + video 8 MiB a quarter done -> (2 + 2) / 12
        progress.Feed("1: [download]  50.0% of 4.00MiB at 1.00MiB/s ETA 00:02 (140)");
        progress.Feed("2: [download]  25.0% of 8.00MiB at 1.00MiB/s ETA 00:05 (134)");

        Assert.Equal(4.0 / 12.0, activity.Progress!.Value, 3);
    }

    [Fact]
    public void BothDestinationsUpFront_DoesNotStartHigh()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        // yt-dlp prints every Destination before any progress when downloading formats concurrently.
        progress.Feed("[download] Destination: video.f134.mp4");
        progress.Feed("[download] Destination: audio.f140.m4a");
        progress.Feed("1: [download]   0.5% of 3.29MiB at 0.00B/s ETA Unknown (140)");
        progress.Feed("2: [download]   0.2% of 8.00MiB at 0.00B/s ETA Unknown (134)");

        Assert.InRange(activity.Progress!.Value, 0, 0.01);
    }

    [Fact]
    public void NeverGoesBackwards_WhenADenominatorGrows()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        progress.Feed("1: [download] 80.0% of 4.00MiB at 1.00MiB/s ETA 00:02 (140)");
        var peak = activity.Progress;
        progress.Feed("2: [download]  0.0% of 8.00MiB at 0.00B/s ETA Unknown (134)");

        Assert.Equal(peak, activity.Progress);
    }

    [Fact]
    public void IgnoresUnrelatedLines()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        progress.Feed("[youtube] Extracting URL: https://youtu.be/x");
        progress.Feed("[download] Destination: video.f134.mp4");
        progress.Feed("[Merger] Merging formats into \"out.mp4\"");

        Assert.Null(activity.Progress);
    }
}
