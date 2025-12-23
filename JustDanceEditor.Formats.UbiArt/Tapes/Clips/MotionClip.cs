namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record MotionClip : Clip
{
    public override string __class { get; } = "MotionClip";
    public string ClassifierPath { get; set; } = string.Empty;
    public int GoldMove { get; set; }
    public int CoachId { get; set; }
    public int MoveType { get; set; }
    public float[] Color { get; set; } = new float[4];
}