namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record VibrationClip : Clip
{
    public override string __class { get; } = "VibrationClip";
    public string VibrationFilePath { get; set; } = string.Empty;
    public int Loop { get; set; }
    public int DeviceSide { get; set; }
    public int PlayerId { get; set; }
    public int Context { get; set; }
    public int StartTimeOffset { get; set; }
    public float Modulation { get; set; }
}