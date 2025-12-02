using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Intermediate.Timelines;

public class CoachTimelineDocument
{
    [JsonPropertyName("coachId")]
    public int CoachId { get; set; }

    [JsonPropertyName("trackId")]
    public long? TrackId { get; set; }

    [JsonPropertyName("clips")]
    public List<CoachTimelineClip> Clips { get; set; } = [];
}

public class CoachTimelineClip
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("startBeat")]
    public int StartTime { get; set; }

    [JsonPropertyName("moveId")]
    public string MoveId { get; set; } = string.Empty;
    [JsonPropertyName("isGoldMove")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsGoldMove { get; set; } = false;
}

public class CoachMoveDefinition
{
    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("moveType")]
    public CoachMoveType MoveType { get; set; }
}

public enum CoachMoveType
{
    HandTracking = 0,
    FullBodyTracking = 1
}
