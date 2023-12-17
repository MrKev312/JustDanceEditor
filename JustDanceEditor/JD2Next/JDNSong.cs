namespace JustDanceEditor.JD2Next;

internal class JDNSong
{
    public string Name { get; set; }

    public KaraokeTape KTape { get; set; }
    public DanceTape DTape { get; set; }
    public MusicTrack MTrack { get; set; }
    public SongDesc SongDesc { get; set; }
}
