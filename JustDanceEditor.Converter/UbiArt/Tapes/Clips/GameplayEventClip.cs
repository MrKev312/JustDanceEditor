namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class GameplayEventClip : IClip
{
    public string __class { get; set; } = "GameplayEventClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public int[] ActorIndices { get; set; } = [];
    public int EventType { get; set; }
    public string CustomParam { get; set; } = "";
}
