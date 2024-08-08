using JustDanceEditor.Converter.UbiArt.Tapes;

using System;

namespace JustDanceEditor.Converter.UbiArt;

public class JDUbiArtSong
{
    public string Name { get => DTape.MapName; set => DTape.MapName = value; }
    public int CoachCount { get => SongDesc.COMPONENTS[0].NumCoach; set => SongDesc.COMPONENTS[0].NumCoach = value; }
    public JDVersion EngineVersion = JDVersion.Unknown;
    public JDVersion JDVersion = JDVersion.Unknown;
    public KaraokeTape KTape { get; set; } = new();
    public DanceTape DTape { get; set; } = new();
    public MusicTrack MTrack { get; set; } = new();
    public MainSequence MainSequence { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();

    public (float start, float end) GetPreviewStartEndTimes(bool isAudio = true)
    {
        float songOffset = 0;

        if (isAudio)
        {
            // Get the startbeat offset
            int songStartBeat = Math.Abs(MTrack.COMPONENTS[0].trackData.structure.startBeat);
            songOffset = -MTrack.COMPONENTS[0].trackData.structure.markers[songStartBeat] / 48f / 1000f;
        }
        else
        {
            songOffset = MTrack.COMPONENTS[0].trackData.structure.videoStartTime;
        }

        // Get the start and end markers
        int startBeat = MTrack.COMPONENTS[0].trackData.structure.previewLoopStart;
        int endBeat = MTrack.COMPONENTS[0].trackData.structure.previewLoopEnd;

        // Convert the ticks to ubiart timing using the markers
        float startTime = MTrack.COMPONENTS[0].trackData.structure.markers[startBeat] / 48f / 1000f;
        float endTime = MTrack.COMPONENTS[0].trackData.structure.markers[endBeat] / 48f / 1000f;

        startTime -= songOffset;
        endTime -= songOffset;

        // For now, force the length to be 30 seconds
        endTime = startTime + 30f;

        return (startTime, endTime);
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