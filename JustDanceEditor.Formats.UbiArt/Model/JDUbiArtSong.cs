using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt.Model;

public class JDUbiArtSong
{
    private const int CommunityMashupBackgroundType = 5;

    public string Name { get; set; } = string.Empty;
    public int CoachCount { get => SongDesc.Components[0].NumCoach; set => SongDesc.Components[0].NumCoach = value; }
    public uint JDVersion { get; set; } = 2022;
    public List<Clip> Clips { get; set; } = [];
    public MusicTrack MusicTrack { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();
    public LegacyMashupData? LegacyMashup { get; set; }
    public bool IsLegacyMashup => LegacyMashup != null;
    public bool IsCommunityMashup
    {
        get
        {
            InfoComponent? info = SongDesc.Components.FirstOrDefault();
            string mapName = string.IsNullOrWhiteSpace(Name) ? info?.MapName ?? string.Empty : Name;
            return info?.BackgroundType == CommunityMashupBackgroundType ||
                info?.Tags.Any(tag => tag.Equals("CommunityMashup", StringComparison.OrdinalIgnoreCase)) == true ||
                IsCommunityMashupName(mapName);
        }
    }

    public bool IsStarRemix
    {
        get
        {
            InfoComponent? info = SongDesc.Components.FirstOrDefault();
            string mapName = string.IsNullOrWhiteSpace(Name) ? info?.MapName ?? string.Empty : Name;
            return info?.Tags.Any(tag => tag.Equals("StarRemix", StringComparison.OrdinalIgnoreCase)) == true ||
                mapName.EndsWith("SR", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsCommunityMashupName(string mapName) =>
        !string.IsNullOrWhiteSpace(mapName) &&
        (mapName.EndsWith("CMU", StringComparison.OrdinalIgnoreCase) ||
         mapName.EndsWith("SR", StringComparison.OrdinalIgnoreCase));

    public float GetAudioStartOffset()
    {
        Structure structure = MusicTrack.Components[0].TrackData.Structure;
        int samplePosition = MusicTrackTiming.GetBeatSamplePosition(structure.Markers, structure.StartBeat);
        return -samplePosition / (float)MusicTrackTiming.SampleRate;
    }
}
