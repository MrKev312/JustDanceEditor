using JustDanceEditor.JDUbiArt.Tapes;

namespace JustDanceEditor.JDUbiArt;

internal class JDUbiArtSong
{
	public string Name { get; set; }
	public KaraokeTape KTape { get; set; }
	public DanceTape DTape { get; set; }
	public MusicTrack MTrack { get; set; }
	public SongDesc SongDesc { get; set; }
}
