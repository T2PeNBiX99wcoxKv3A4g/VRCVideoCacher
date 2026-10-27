using System.Globalization;
using System.Text.RegularExpressions;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.YTDL;

/// <summary>
/// Turns yt-dlp's per-stream <c>[download]  42.3% of 10.00MiB</c> lines into a single monotonic 0..1
/// fraction and reports it to a status activity. Merged downloads run their streams concurrently
/// (yt-dlp prefixes them <c>1:</c>/<c>2:</c> and prints every Destination up front), so the fraction is
/// byte-weighted across all streams seen rather than assuming they run one after another.
/// </summary>
internal sealed partial class YtdlProgress(StatusActivity activity)
{
    private readonly Dictionary<int, (long Total, double Percent)> _streams = [];
    private double _reported = -1;

    /// <summary>Feed one line of yt-dlp output. Non-progress lines are ignored.</summary>
    public void Feed(string line)
    {
        var match = ProgressRegex().Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ||
            !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
            return;

        var total = (long)(size * UnitFactor(match.Groups[4].Value));
        if (total <= 0)
            return;

        var stream = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        _streams[stream] = (total, Math.Clamp(percent, 0, 100));

        long totalBytes = 0, doneBytes = 0;
        foreach (var (streamTotal, streamPercent) in _streams.Values)
        {
            totalBytes += streamTotal;
            doneBytes += (long)(streamTotal * streamPercent / 100.0);
        }

        // Byte-weighted across every stream, and never allowed to go backwards so a late-arriving
        // stream (or a re-reported one) cannot make the bar jump back down.
        var fraction = Math.Clamp((double)doneBytes / totalBytes, 0, 1);
        if (fraction <= _reported)
            return;
        if (fraction - _reported < 0.01 && fraction < 1.0) // throttle to ~1% steps
            return;

        _reported = fraction;
        activity.Report(fraction);
    }

    private static double UnitFactor(string unit) => unit switch
    {
        "KiB" or "KB" => 1024d,
        "MiB" or "MB" => 1024d * 1024,
        "GiB" or "GB" => 1024d * 1024 * 1024,
        "TiB" or "TB" => 1024d * 1024 * 1024 * 1024,
        _ => 1d
    };

    [GeneratedRegex(@"^(?:(\d+): )?\[download\]\s+([\d.]+)% of\s+~?([\d.]+)([KMGTP]?i?B)\b")]
    private static partial Regex ProgressRegex();
}