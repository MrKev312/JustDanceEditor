using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt.Model;

public class JDUbiArtSong
{
    public string Name { get; set; } = string.Empty;
    public int CoachCount { get => SongDesc.Components[0].NumCoach; set => SongDesc.Components[0].NumCoach = value; }
    public uint JDVersion { get; set; } = 2022;
    public List<Clip> Clips { get; set; } = [];
    public MusicTrack MusicTrack { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();
    public LegacyMashupData? LegacyMashup { get; set; }
    public bool IsLegacyMashup => LegacyMashup != null;

    public float GetAudioStartOffset()
    {
        Structure structure = MusicTrack.Components[0].TrackData.Structure;
        int samplePosition = MusicTrackTiming.GetBeatSamplePosition(structure.Markers, structure.StartBeat);
        return -samplePosition / (float)MusicTrackTiming.SampleRate;
    }
}