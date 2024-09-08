using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;

namespace JustDanceEditor.Converter.UbiArt;

public class JDUbiArtSong
{
    public string Name { get; set; } = "";
    public uint CoachCount { get => SongDesc.COMPONENTS[0].NumCoach; set => SongDesc.COMPONENTS[0].NumCoach = value; }
    public uint EngineVersion = (uint)DateTime.Now.Year;
    public uint JDVersion = 2022;
    public List<IClip> Clips { get; set; } = [];
    public MusicTrack MusicTrack { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();

    public float GetPreviewStartTime(bool isAudio = true)
    {
        float songOffset;

        if (isAudio)
        {
            // Get the startbeat offset
            songOffset = -GetSongStartTime();
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
        if (beat > 0)
            time = -time;

        return time;
    }
}
