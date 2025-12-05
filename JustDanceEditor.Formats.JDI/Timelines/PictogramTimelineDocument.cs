namespace JustDanceEditor.Formats.JDI.Timelines;

public class PictogramTimelineDocument
{
    public List<PictogramEntry> Entries { get; set; } = [];
}

public class PictogramEntry : TimelineClipBase
{
    public int Duration { get; set; }
    public string PictogramId { get; set; } = string.Empty;
    public int CoachCount { get; set; }
}