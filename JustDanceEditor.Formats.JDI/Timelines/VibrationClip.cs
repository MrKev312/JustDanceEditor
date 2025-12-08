namespace JustDanceEditor.Formats.JDI.Timelines;

public class VibrationClip : TimelineClipBase
{
    public long TrackId { get; set; }
    public bool IsActive { get; set; }
    public int Duration { get; set; }
}