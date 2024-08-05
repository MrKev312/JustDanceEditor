namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class VibrationClip : IClip
{
    public string __class { get; set; } = "VibrationClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string VibrationFilePath { get; set; } = "";
    public int Loop { get; set; }
    public int DeviceSide { get; set; }
    public int PlayerId { get; set; }
    public int Context { get; set; }
    public int StartTimeOffset { get; set; }
    public float Modulation { get; set; }
}
