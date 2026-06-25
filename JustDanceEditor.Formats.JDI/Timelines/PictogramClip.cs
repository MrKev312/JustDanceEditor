namespace JustDanceEditor.Formats.JDI.Timelines;

public class PictogramClip : TimelineClipBase
{
    public int Duration { get; set; }
    public string PictogramId { get; set; } = string.Empty;
    public int CoachCount { get; set; }
}