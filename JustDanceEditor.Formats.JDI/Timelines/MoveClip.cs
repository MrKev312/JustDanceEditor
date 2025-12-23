using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class MoveClip : TimelineClipBase
{
    public string MoveId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsGoldMove { get; set; } = false;
}

public class MoveTimeline : Timeline<MoveClip>
{
    public int CoachId { get; set; }
    public long TrackId { get; set; }
}

public class CoachMoveDefinition
{
    public string Color { get; set; } = "#CCCCCC";
    public int Duration { get; set; }
    public CoachMoveType MoveType { get; set; }
}

public enum CoachMoveType
{
    HandTracking = 0,
    FullBodyTracking = 1
}