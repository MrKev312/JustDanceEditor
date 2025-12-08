namespace JustDanceEditor.Formats.JDI.Timelines;

public class GoldEffectClip : TimelineClipBase
{
    public long TrackId { get; set; }
    public bool IsActive { get; set; }
    public int Duration { get; set; }
    public int EffectType { get; set; }
}
