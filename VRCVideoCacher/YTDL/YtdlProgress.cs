using System.Globalization;
using System.Text.RegularExpressions;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.YTDL;

/// <summary>
/// Turns yt-dlp's <c>[download]  42.3% of ...</c> lines into a monotonic 0..1 fraction and reports it to a
/// status activity. A merged download (e.g. video+audio) runs in phases; the first phase is weighted heavier
/// so the bar advances smoothly instead of snapping back to 0 when the second stream starts.
/// </summary>
internal sealed partial class YtdlProgress(StatusActivity activity, int phases = 1)
{
    private readonly double[] _weights = phases == 2 ? [0.85, 0.15] : [1.0];
    private int _destinations;
    private double _last = -1;
    private double _reported = -1;

    /// <summary>Feed one line of yt-dlp output. Non-progress lines are ignored.</summary>
    public void Feed(string line)
    {
        if (line.Contains("[download] Destination:", StringComparison.Ordinal))
        {
            _destinations++;
            return;
        }

        var match = PercentRegex().Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return;

        var phase = Math.Clamp(_destinations - 1, 0, _weights.Length - 1);
        var start = 0.0;
        for (var i = 0; i < phase; i++)
            start += _weights[i];

        var fraction = Math.Clamp(start + _weights[phase] * (percent / 100.0), 0, 1);
        if (fraction <= _last) // progress must never go backwards
            return;

        _last = fraction;
        if (fraction - _reported < 0.01 && fraction < 1.0) // throttle to ~1% steps
            return;

        _reported = fraction;
        activity.Report(fraction);
    }

    [GeneratedRegex(@"\[download\]\s+([\d.]+)%")]
    private static partial Regex PercentRegex();
}
