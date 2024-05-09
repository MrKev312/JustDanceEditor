using JustDanceEditor.Converter.UbiArt.Tapes;

namespace JustDanceEditor.Converter.UbiArt;

public class JDUbiArtSong
{
    public string Name { get => DTape.MapName; set => DTape.MapName = value; }
    public KaraokeTape KTape { get; set; }
    public DanceTape DTape { get; set; }
    public MusicTrack MTrack { get; set; }
    public SongDesc SongDesc { get; set; }
}
