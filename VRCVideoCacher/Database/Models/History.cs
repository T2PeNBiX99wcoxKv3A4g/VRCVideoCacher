using System.ComponentModel.DataAnnotations;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Database.Models;

public class History
{
    [Key] public int Key { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Url { get; init; }
    public required string? Id { get; init; }
    public required UrlType Type { get; init; }
}