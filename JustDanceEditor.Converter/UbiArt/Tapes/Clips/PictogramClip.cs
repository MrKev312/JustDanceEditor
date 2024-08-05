namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class PictogramClip : IClip
{
    public string __class { get; set; } = "PictogramClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string PictoPath { get; set; } = "";
    public long CoachCount { get; set; }
}
