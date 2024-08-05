namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class GoldEffectClip : IClip
{
    public string __class { get; set; } = "GoldEffectClip";
    public int EffectType { get; set; }
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
}
