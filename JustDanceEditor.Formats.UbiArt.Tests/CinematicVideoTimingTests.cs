using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Intermediate;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicVideoTimingTests
{
    [Fact]
    public void MasterVideoDuration_UsesMusicTrackEndBeatVideoTime()
    {
        IntermediateSongPackage package = new()
        {
            TimelineStructure = CreateTimelineStructure()
        };

        double duration = IntermediateAssetWriter.GetMasterVideoDurationSeconds(package, fallbackDurationSeconds: 99);

        Assert.Equal(4.75, duration, precision: 6);
    }

    [Fact]
    public void CinematicRenderDuration_AddsFiveSecondSafetyTail()
    {
        IntermediateSongPackage package = new()
        {
            TimelineStructure = CreateTimelineStructure()
        };

        double duration = IntermediateAssetWriter.GetCinematicRenderDurationSeconds(package, fallbackDurationSeconds: 99);

        Assert.Equal(9.75, duration, precision: 6);
    }

    [Fact]
    public void RenderFrameCount_UsesFullRenderDuration()
    {
        int frameCount = CinematicVisualRenderer.GetRenderFrameCount(224.795);

        Assert.Equal(5620, frameCount);
    }

    [Fact]
    public void RenderFrameCount_RoundsUpToAtLeastOneFrame()
    {
        int frameCount = CinematicVisualRenderer.GetRenderFrameCount(0.001);

        Assert.Equal(1, frameCount);
    }

    private static TimelineStructureDocument CreateTimelineStructure() => new()
    {
        StartBeat = -2,
        EndBeat = 4,
        VideoStartOffset = -0.75,
        Markers =
        [
            0,
            48000,
            96000,
            144000,
            192000,
            240000
        ]
    };
}