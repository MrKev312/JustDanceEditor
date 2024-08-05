namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public interface IClip
{
    public string __class { get; set; }
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
}
