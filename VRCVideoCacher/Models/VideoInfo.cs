// ReSharper disable InconsistentNaming

namespace VRCVideoCacher.Models;

public enum UrlType
{
    YouTube,
    PyPyDance,
    VRDancing,
    NicoNico,
    Other
}

public enum DownloadFormat
{
    MP4,
    Webm
}

public class VideoInfo
{
    public required string VideoUrl;
    public required string VideoId;
    public required UrlType UrlType;
    public required DownloadFormat DownloadFormat;
}