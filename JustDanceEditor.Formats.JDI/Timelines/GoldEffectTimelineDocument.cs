using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class GoldEffectTimelineDocument
{
    [JsonPropertyName("clips")]
    public List<GoldEffectTimelineClip> Clips { get; set; } = [];
}

public class GoldEffectTimelineClip
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("startTime")]
    public int StartTime { get; set; }

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

public class HideUserInterfaceTimelineClip
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("startTime")]
    public int StartTime { get; set; }

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

public class GameplayEventTimelineClip
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("startTime")]
    public int StartTime { get; set; }

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

public class VibrationTimelineClip
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("startTime")]
    public int StartTime { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }
}
