namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public class TapeReferenceClip : IClip
{
    public string __class { get; set; } = "TapeReferenceClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string Path { get; set; } = string.Empty;
    public int Loop { get; set; }
}
