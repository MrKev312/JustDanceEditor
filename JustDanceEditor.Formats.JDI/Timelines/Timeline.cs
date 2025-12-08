namespace JustDanceEditor.Formats.JDI.Timelines;

public class Timeline<TClip> where TClip : TimelineClipBase
{
    public List<TClip> Clips { get; set; } = [];
}