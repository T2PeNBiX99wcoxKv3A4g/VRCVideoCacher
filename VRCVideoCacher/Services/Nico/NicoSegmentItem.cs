namespace VRCVideoCacher.Services.Nico;

internal sealed class NicoSegmentItem
{
    public string Url { get; set; } = "";
    public double Duration { get; set; }
    public string? KeyUrl { get; set; }
    public string? KeyIv { get; set; }
}
