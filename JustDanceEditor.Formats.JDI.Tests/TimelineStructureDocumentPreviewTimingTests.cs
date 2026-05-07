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

        Assert.Equal(7, start.TotalSeconds);
        Assert.Equal(30, duration.TotalSeconds);
    }

    [Fact]
    public void GetVideoPreviewTiming_MatchesEditorVideoClock()
    {
        TimelineStructureDocument structure = CreateStructure();

        (TimeSpan start, TimeSpan duration) = structure.GetVideoPreviewTiming();

        Assert.Equal(4.75, start.TotalSeconds);
        Assert.Equal(30, duration.TotalSeconds);
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
