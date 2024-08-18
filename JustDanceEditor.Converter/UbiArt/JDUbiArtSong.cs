using JustDanceEditor.Converter.UbiArt.Tapes;

namespace JustDanceEditor.Converter.UbiArt;

public class JDUbiArtSong
{
    public string Name { get => DanceTape.MapName; set => DanceTape.MapName = value; }
    public uint CoachCount { get => SongDesc.COMPONENTS[0].NumCoach; set => SongDesc.COMPONENTS[0].NumCoach = value; }
    public JDVersion EngineVersion = JDVersion.Unknown;
    public JDVersion JDVersion = JDVersion.Unknown;
    public ClipTape KaraokeTape { get; set; } = new();
    public ClipTape DanceTape { get; set; } = new();
    public MusicTrack MusicTrack { get; set; } = new();
    public ClipTape MainSequence { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();

    public float GetPreviewStartTime(bool isAudio = true)
    {
        float songOffset;

        if (isAudio)
        {
            // Get the startbeat offset
            int songStartBeat = Math.Abs(MusicTrack.COMPONENTS[0].trackData.structure.startBeat);
            songOffset = MusicTrack.COMPONENTS[0].trackData.structure.markers[songStartBeat] / 48f / 1000f;
        }
        else
        {
            songOffset = MusicTrack.COMPONENTS[0].trackData.structure.videoStartTime;
        }

        // Get the start and end markers
        int startBeat = MusicTrack.COMPONENTS[0].trackData.structure.previewLoopStart;

        // Convert the ticks to ubiart timing using the markers
        float startTime = MusicTrack.COMPONENTS[0].trackData.structure.markers[startBeat] / 48f / 1000f;

        startTime -= songOffset;

        return startTime;
    }

    public float GetSongStartTime()
    {
        int beat = MusicTrack.COMPONENTS[0].trackData.structure.startBeat;

        // Get the absolute of the start beat
        int marker = Math.Abs(beat);

        float time = MusicTrack.COMPONENTS[0].trackData.structure.markers[marker] / 48f / 1000f;

        // Set opposite sign
        if (beat < 0)
            time = -time;

        return time;
    }
}

public enum JDVersion
{
    Unknown = -1,
    JDUnlimited = 0,
    JD1,
    JD2,
    JD3,
    JD4,
    JD2014,
    JD2015,
    JD2016,
    JD2017,
    JD2018,
    JD2019,
    JD2020,
    JD2021,
    JD2022
}