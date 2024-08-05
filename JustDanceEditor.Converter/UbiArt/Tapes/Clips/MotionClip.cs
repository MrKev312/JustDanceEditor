namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class MotionClip : IClip
{
    public string __class { get; set; } = "MotionClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string ClassifierPath { get; set; } = "";
    public int GoldMove { get; set; }
    public int CoachId { get; set; }
    public int MoveType { get; set; }
    public int EffectType { get; set; }
}