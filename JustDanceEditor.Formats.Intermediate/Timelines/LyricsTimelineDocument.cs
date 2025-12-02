using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Intermediate.Timelines;

public class LyricsTimelineDocument
{
    [JsonPropertyName("clips")]
    public List<KaraokeClip> Clips { get; set; } = [];
}

public class KaraokeClip
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("startBeat")]
    public int StartTime { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("lyrics")]
    public string Lyrics { get; set; } = string.Empty;

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; }

    [JsonPropertyName("isEndOfLine")]
    public bool IsEndOfLine { get; set; }

    [JsonPropertyName("contentType")]
    public int ContentType { get; set; }

    [JsonPropertyName("tolerances")]
    public KaraokeTolerance? Tolerances { get; set; }
}

public class KaraokeTolerance
{
    [JsonPropertyName("startBeatTolerance")]
    public int StartTimeTolerance { get; set; }

    [JsonPropertyName("endBeatTolerance")]
    public int EndTimeTolerance { get; set; }

    [JsonPropertyName("semitoneTolerance")]
    public double SemitoneTolerance { get; set; }
}
