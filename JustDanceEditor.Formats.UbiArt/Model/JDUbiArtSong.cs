using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt;

public class JDUbiArtSong
{
    public string Name { get; set; } = string.Empty;
    public int CoachCount { get => SongDesc.Components[0].NumCoach; set => SongDesc.Components[0].NumCoach = value; }
    public uint JDVersion { get; set; } = 2022;
    public List<Clip> Clips { get; set; } = [];
    public MusicTrack MusicTrack { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();

    public float GetSongStartTime()
    {
        int beat = MusicTrack.Components[0].TrackData.Structure.StartBeat;
        int marker = Math.Abs(beat);
        float time = MusicTrack.Components[0].TrackData.Structure.Markers[marker] / 48f / 1000f;

        if (beat > 0)
            time = -time;

        return time;
    }
}