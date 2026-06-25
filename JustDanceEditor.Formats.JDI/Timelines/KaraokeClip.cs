using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class KaraokeClip : TimelineClipBase
{
    public int Duration { get; set; }
    public string Lyrics { get; set; } = string.Empty;
    public float Pitch { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsEndOfLine { get; set; }
    public int ContentType { get; set; }
    public KaraokeTolerance? Tolerances { get; set; }
}

public class KaraokeTolerance
{
    public int StartTimeTolerance { get; set; }
    public int EndTimeTolerance { get; set; }
    public double SemitoneTolerance { get; set; }
}