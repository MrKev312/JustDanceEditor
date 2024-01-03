namespace JustDanceEditor.JDUbiArt;

public class DanceTape
{
    public string __class { get; set; }
    public MotionClip[] Clips { get; set; }
    public int TapeClock { get; set; }
    public int TapeBarCount { get; set; }
    public int FreeResourcesAfterPlay { get; set; }
    public string MapName { get; set; }
    public string SoundwichEvent { get; set; }
}

public class MotionClip
{
    public string __class { get; set; }
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string ClassifierPath { get; set; }
    public int GoldMove { get; set; }
    public int CoachId { get; set; }
    public int MoveType { get; set; }
    public int EffectType { get; set; }
    public string PictoPath { get; set; }
    public long CoachCount { get; set; }
}