using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Intermediate;

using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using SixLabors.ImageSharp;

using System.Numerics;

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

    [Fact]
    public void GraphVideoFilter_CoverCropsDanDanViewportWithoutStretching()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(0, -1),
            new Vector2(1920, -1),
            new Vector2(1920, 1081),
            new Vector2(0, 1081),
            new Rectangle(0, -1, 1920, 1082));
        CinematicSingleVideoScene scene = new("world/maps/dandandubizuba/videoscoach/dandandubizuba.webm", "video/videooutput", outputQuad, 1920, 1080);

        Rectangle crop = IntermediateAssetWriter.CalculateGraphSourceCrop(1216, 720, scene);
        string filter = IntermediateAssetWriter.BuildGraphVideoFilter(1216, 720, scene);

        Assert.Equal(new Rectangle(0, 18, 1216, 684), crop);
        Assert.Equal("crop=1216:684:0:18,setsar=1", filter);
    }

    [Fact]
    public void GraphVideoFilter_ScalesSimpleFourByThreeFillTo1080p()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(0, 0),
            new Vector2(1920, 0),
            new Vector2(1920, 1080),
            new Vector2(0, 1080),
            new Rectangle(0, 0, 1920, 1080));
        CinematicSingleVideoScene scene = new("world/maps/7rings/videoscoach/7rings.wii.webm", "video/videooutput", outputQuad, 1920, 1080);

        Rectangle crop = IntermediateAssetWriter.CalculateGraphSourceCrop(512, 384, scene);
        string filter = IntermediateAssetWriter.BuildGraphVideoFilter(512, 384, scene);

        Assert.Equal(new Rectangle(0, 48, 512, 288), crop);
        Assert.Equal("crop=512:288:0:48,scale=1920:1080,setsar=1", filter);
    }

    [Fact]
    public void SceneVideoMatch_AcceptsPlatformQualitySuffix()
    {
        CookedFile source = new("world/maps/DanDanDubiZuba/videoscoach/dandandubizuba.vp9.720.webm");

        bool matches = CinematicPrerenderedVideoAnalyzer.MatchesSourceVideo(
            source,
            "world/maps/dandandubizuba/videoscoach/dandandubizuba.webm");

        Assert.True(matches);
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
