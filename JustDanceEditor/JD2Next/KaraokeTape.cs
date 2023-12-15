namespace JustDanceEditor.JD2Next;

public class KaraokeTape
{
    public string __class { get; set; }
    public KaraokeClip[] Clips { get; set; }
    public int TapeClock { get; set; }
    public int TapeBarCount { get; set; }
    public int FreeResourcesAfterPlay { get; set; }
    public string MapName { get; set; }
    public string SoundwichEvent { get; set; }
}

public class KaraokeClip
{
    public string __class { get; set; }
    public long Id { get; set; }
    public int TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public float Pitch { get; set; }
    public string Lyrics { get; set; }
    public int IsEndOfLine { get; set; }
    public int ContentType { get; set; }
    public int StartTimeTolerance { get; set; }
    public int EndTimeTolerance { get; set; }
    public int SemitoneTolerance { get; set; }
}

