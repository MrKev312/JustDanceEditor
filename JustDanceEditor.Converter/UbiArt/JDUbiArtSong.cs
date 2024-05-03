using JustDanceEditor.Converter.UbiArt.Tapes;

namespace JustDanceEditor.Converter.UbiArt;

internal class JDUbiArtSong
{
    public string Name { get; set; }
    public KaraokeTape KTape { get; set; }
    public DanceTape DTape { get; set; }
    public MusicTrack MTrack { get; set; }
    public SongDesc SongDesc { get; set; }
}
