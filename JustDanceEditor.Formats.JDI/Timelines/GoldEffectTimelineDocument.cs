namespace JustDanceEditor.Formats.JDI.Timelines;

public class GoldEffectTimelineDocument
{
    public List<GoldEffectTimelineClip> Clips { get; set; } = [];
}

public class GoldEffectTimelineClip : TimelineClipBase
{
    public long TrackId { get; set; }
    public bool IsActive { get; set; }
    public int Duration { get; set; }
    public int EffectType { get; set; }
}

public class HideUserInterfaceTimelineDocument
{
    public List<HideUserInterfaceTimelineClip> Clips { get; set; } = [];
}

public class HideUserInterfaceTimelineClip : TimelineClipBase
{
    public bool IsActive { get; set; }
    public int Duration { get; set; }
}

public class VibrationTimelineDocument
{
    public List<VibrationTimelineClip> Clips { get; set; } = [];
}

public class VibrationTimelineClip : TimelineClipBase
{
    public long TrackId { get; set; }
    public bool IsActive { get; set; }
    public int Duration { get; set; }
}
