using System.Text.Json.Serialization;

namespace VRCVideoCacher.Services.Nico;

public class NicoWatchPageData
{
    [JsonPropertyName("data")] public NicoWatchData? Data { get; set; }
    [JsonPropertyName("program")] public NicoLiveProgram? Program { get; set; }
}

public class NicoWatchData
{
    [JsonPropertyName("response")] public NicoWatchResponse? Response { get; set; }
}

public class NicoWatchResponse
{
    [JsonPropertyName("video")] public NicoWatchVideo? Video { get; set; }
    [JsonPropertyName("tag")] public NicoWatchTagContainer? Tag { get; set; }
    [JsonPropertyName("media")] public NicoWatchMedia? Media { get; set; }
    [JsonPropertyName("client")] public NicoWatchClient? Client { get; set; }
}

public class NicoWatchVideo
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("duration")] public long? Duration { get; set; }
    [JsonPropertyName("thumbnail")] public NicoWatchThumbnail? Thumbnail { get; set; }
    [JsonPropertyName("count")] public NicoWatchCount? Count { get; set; }
}

public class NicoWatchThumbnail
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("player")] public string? Player { get; set; }
}

public class NicoWatchCount
{
    [JsonPropertyName("view")] public long? View { get; set; }
    [JsonPropertyName("comment")] public long? Comment { get; set; }
    [JsonPropertyName("mylist")] public long? MyList { get; set; }
    [JsonPropertyName("like")] public long? Like { get; set; }
}

public class NicoWatchTagContainer
{
    [JsonPropertyName("items")] public List<NicoWatchTagItem>? Items { get; set; }
}

public class NicoWatchTagItem
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class NicoWatchMedia
{
    [JsonPropertyName("domand")] public NicoWatchDomand? Domand { get; set; }
}

public class NicoWatchDomand
{
    [JsonPropertyName("accessRightKey")] public string? AccessRightKey { get; set; }
    [JsonPropertyName("audios")] public List<NicoWatchAudioVideoItem>? Audios { get; set; }
    [JsonPropertyName("videos")] public List<NicoWatchAudioVideoItem>? Videos { get; set; }
}

public class NicoWatchAudioVideoItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("isAvailable")] public bool? IsAvailable { get; set; }
}

public class NicoWatchClient
{
    [JsonPropertyName("watchTrackId")] public string? WatchTrackId { get; set; }
    [JsonPropertyName("nicosid")] public string? NicosId { get; set; }
}

public class NicoLiveProgram
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("statistics")] public NicoLiveStatistics? Statistics { get; set; }
}

public class NicoLiveStatistics
{
    [JsonPropertyName("watchCount")] public long? WatchCount { get; set; }
    [JsonPropertyName("commentCount")] public long? CommentCount { get; set; }
}

public class NicoNvApiAccessRightsRequest
{
    [JsonPropertyName("outputs")] public List<string[]> Outputs { get; set; } = [];
}

public class NicoNvApiAccessRightsResponse
{
    [JsonPropertyName("data")] public NicoNvApiAccessRightsData? Data { get; set; }
}

public class NicoNvApiAccessRightsData
{
    [JsonPropertyName("contentUrl")] public string? ContentUrl { get; set; }
}

[JsonSourceGenerationOptions(
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NicoWatchPageData))]
[JsonSerializable(typeof(NicoNvApiAccessRightsRequest))]
[JsonSerializable(typeof(NicoNvApiAccessRightsResponse))]
internal partial class NicoJsonContext : JsonSerializerContext
{
}
