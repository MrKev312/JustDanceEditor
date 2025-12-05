using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class GoldEffectTimelineDocument
{
    [JsonPropertyName("clips")]
    public List<GoldEffectTimelineClip> Clips { get; set; } = [];
}

public class GoldEffectTimelineClip : TimelineClipBase
{
    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("effectType")]
    public int EffectType { get; set; }
}

public class HideUserInterfaceTimelineDocument
{
    [JsonPropertyName("clips")]
    public List<HideUserInterfaceTimelineClip> Clips { get; set; } = [];
}

public class HideUserInterfaceTimelineClip : TimelineClipBase
{
    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("eventType")]
    public int EventType { get; set; }

    [JsonPropertyName("customParam")]
    public string CustomParam { get; set; } = string.Empty;
}

public class GameplayEventTimelineDocument
{
    [JsonPropertyName("clips")]
    public List<GameplayEventTimelineClip> Clips { get; set; } = [];
}

public class GameplayEventTimelineClip : TimelineClipBase
{
    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("eventName")]
    public string EventName { get; set; } = string.Empty;
}

public class VibrationTimelineDocument
{
    [JsonPropertyName("clips")]
    public List<VibrationTimelineClip> Clips { get; set; } = [];
}

public class VibrationTimelineClip : TimelineClipBase
{
    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }
}
