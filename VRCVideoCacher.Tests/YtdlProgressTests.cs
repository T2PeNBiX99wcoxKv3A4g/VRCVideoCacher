using VRCVideoCacher.Services;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Tests;

public class YtdlProgressTests
{
    private static StatusActivity NewActivity() =>
        StatusService.Begin(StatusCategory.Downloading, "test");

    [Fact]
    public void SinglePhase_ReportsPercent()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        progress.Feed("[download]  42.0% of 10.00MiB at 1.00MiB/s ETA 00:05");

        Assert.Equal(0.42, activity.Progress!.Value, 3);
    }

    [Fact]
    public void TwoPhases_WeightVideoThenAudio()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity, 2);

        progress.Feed("[download] Destination: video.f137.mp4");
        progress.Feed("[download] 100.0% of 10.00MiB");
        Assert.Equal(0.85, activity.Progress!.Value, 3);

        progress.Feed("[download] Destination: audio.f140.m4a");
        progress.Feed("[download]  50.0% of 1.00MiB");
        Assert.Equal(0.925, activity.Progress!.Value, 3);

        progress.Feed("[download] 100.0% of 1.00MiB");
        Assert.Equal(1.0, activity.Progress!.Value, 3);
    }

    [Fact]
    public void NeverGoesBackwards()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        progress.Feed("[download] 80.0% of 10.00MiB");
        var peak = activity.Progress;
        progress.Feed("[download] 10.0% of 10.00MiB");

        Assert.Equal(peak, activity.Progress);
    }

    [Fact]
    public void IgnoresUnrelatedLines()
    {
        using var activity = NewActivity();
        var progress = new YtdlProgress(activity);

        progress.Feed("[youtube] Extracting URL: https://youtu.be/x");
        progress.Feed("[info] Downloading webpage");

        Assert.Null(activity.Progress);
    }
}
