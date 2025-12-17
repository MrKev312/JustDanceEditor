using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

namespace JustDanceEditor.Formats.UbiArt;

public class JDUbiArtSong
{
    public string Name { get; set; } = string.Empty;
    public int CoachCount { get => SongDesc.COMPONENTS[0].NumCoach; set => SongDesc.COMPONENTS[0].NumCoach = value; }
    public uint EngineVersion = (uint)DateTime.Now.Year;
    public uint JDVersion = 2022;
    public List<Clip> Clips { get; set; } = [];
    public MusicTrack MusicTrack { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();

    public float GetSongStartTime()
    {
        int beat = MusicTrack.COMPONENTS[0].trackData.structure.startBeat;
        int marker = Math.Abs(beat);
        float time = MusicTrack.COMPONENTS[0].trackData.structure.markers[marker] / 48f / 1000f;

        if (beat > 0)
            time = -time;

        return time;
    }
}