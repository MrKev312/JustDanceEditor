namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class KaraokeClip : IClip
{
    public string __class { get; set; } = "";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public float Pitch { get; set; }
    public string Lyrics { get; set; } = "";
    public int IsEndOfLine { get; set; }
    public int ContentType { get; set; }
    public int StartTimeTolerance { get; set; }
    public int EndTimeTolerance { get; set; }
    public float SemitoneTolerance { get; set; }
}
