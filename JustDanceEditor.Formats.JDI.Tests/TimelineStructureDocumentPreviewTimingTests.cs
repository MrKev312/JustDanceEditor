using JustDanceEditor.Formats.JDI.Timelines;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class TimelineStructureDocumentPreviewTimingTests
{
    [Fact]
    public void GetAudioPreviewTiming_UsesPreviewLoopBeatLabel()
    {
        TimelineStructureDocument structure = CreateStructure();

        (TimeSpan start, TimeSpan duration) = structure.GetAudioPreviewTiming();

        Assert.Equal(5.5, start.TotalSeconds);
        Assert.Equal(30, duration.TotalSeconds);
    }

    [Fact]
    public void GetVideoPreviewTiming_MatchesEditorVideoClock()
    {
        TimelineStructureDocument structure = CreateStructure();

        (TimeSpan start, TimeSpan duration) = structure.GetVideoPreviewTiming();

        Assert.Equal(1.75, start.TotalSeconds);
        Assert.Equal(30, duration.TotalSeconds);
    }

    [Fact]
    public void PlaybackConversions_PreserveTempoChangesAndStartBeatOffset()
    {
        TimelineStructureDocument structure = CreateStructure();

        Assert.Equal(5.5, structure.GetPlaybackSecondsAtBeat(2), precision: 6);
        Assert.Equal(7.5, structure.GetPlaybackSecondsAtBeat(3), precision: 6);
        Assert.Equal(2.0, structure.GetBeatAtPlaybackSeconds(5.5), precision: 6);
        Assert.Equal(3.0, structure.GetBeatAtPlaybackSeconds(7.5), precision: 6);
    }

    [Fact]
    public void NegativeBeatTiming_UsesSameFourMarkerAverageAsUbiArt()
    {
        TimelineStructureDocument structure = CreateStructure();

        Assert.Equal(-3.5, structure.GetSongStartOffset(), precision: 6);
    }

    private static TimelineStructureDocument CreateStructure() => new()
    {
        StartBeat = -2,
        EndBeat = 5,
        VideoStartOffset = 0.25,
        PreviewLoopStartBeat = 2,
        Markers =
        [
            0,
            48000,
            96000,
            192000,
            336000,
            528000,
            768000,
            1056000
        ]
    };
}