using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Intermediate;

using KevInc.UbiArt.FileSystem;

using SixLabors.ImageSharp;

using System;
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
    public void GraphVideoFilter_MapsEntireTextureInsteadOfInferringCoverCrop()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(0, -1),
            new Vector2(1920, -1),
            new Vector2(1920, 1081),
            new Vector2(0, 1081),
            new Rectangle(0, -1, 1920, 1082));
        CinematicSingleVideoScene scene = new("world/maps/dandandubizuba/videoscoach/dandandubizuba.webm", "video/videooutput", outputQuad, 1920, 1080);

        string filter = IntermediateAssetWriter.BuildGraphVideoFilter(1216, 720, scene);

        Assert.Equal("scale=1280:720:flags=bilinear,setsar=1", filter);
        Assert.DoesNotContain("crop", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphVideoFilter_StretchesWiiTextureAtItsNativeHeight()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(0, 0),
            new Vector2(1920, 0),
            new Vector2(1920, 1080),
            new Vector2(0, 1080),
            new Rectangle(0, 0, 1920, 1080));
        CinematicSingleVideoScene scene = new("world/maps/7rings/videoscoach/7rings.wii.webm", "video/videooutput", outputQuad, 1920, 1080);

        string filter = IntermediateAssetWriter.BuildGraphVideoFilter(512, 384, scene);

        Assert.Equal("scale=682:384:flags=bilinear,setsar=1", filter);
    }

    [Fact]
    public void GraphVideoFilter_StretchesWiiUTextureTo720p()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(0, 0),
            new Vector2(1920, 0),
            new Vector2(1920, 1080),
            new Vector2(0, 1080),
            new Rectangle(0, 0, 1920, 1080));
        CinematicSingleVideoScene scene = new("world/maps/pocoloco/videoscoach/pocoloco.webm", "video/videooutput", outputQuad, 1920, 1080);

        (int width, int height) = IntermediateAssetWriter.CalculateGraphVideoOutputSize(1216, 720, scene);
        string filter = IntermediateAssetWriter.BuildGraphVideoFilter(1216, 720, scene);

        Assert.Equal((1280, 720), (width, height));
        Assert.Equal("scale=1280:720:flags=bilinear,setsar=1", filter);
    }

    [Fact]
    public void GraphVideoFilter_MapsAuthoredOverscanThroughFixedFramebuffer()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(-96, -54),
            new Vector2(2016, -54),
            new Vector2(2016, 1134),
            new Vector2(-96, 1134),
            new Rectangle(-96, -54, 2112, 1188));
        CinematicSingleVideoScene scene = new("world/maps/example/videoscoach/example.webm", "video/videooutput", outputQuad, 1920, 1080);

        string filter = IntermediateAssetWriter.BuildGraphVideoFilter(1280, 720, scene);

        Assert.Equal(
            "scale=1920:1080:flags=bilinear,perspective=x0=-96:y0=-54:x1=2016:y1=-54:x2=-96:y2=1134:x3=2016:y3=1134:sense=destination:interpolation=linear,setsar=1",
            filter);
    }

    [Fact]
    public void GraphVideoTransform_PreservesAnyFullscreenSourceAlreadyAtOutputAspect()
    {
        ProjectedQuad outputQuad = new(
            new Vector2(0, -0.75f),
            new Vector2(1920, -0.75f),
            new Vector2(1920, 1080.75f),
            new Vector2(0, 1080.75f),
            new Rectangle(0, -1, 1920, 1082));
        CinematicSingleVideoScene scene = new("world/maps/example/videoscoach/example.webm", "video/videooutput", outputQuad, 1920, 1080);

        Assert.True(IntermediateAssetWriter.CanUseGraphVideoSourceDirectly(1920, 1080, scene));
        Assert.True(IntermediateAssetWriter.CanUseGraphVideoSourceDirectly(1280, 720, scene));
        Assert.False(IntermediateAssetWriter.CanUseGraphVideoSourceDirectly(1216, 720, scene));
        Assert.False(IntermediateAssetWriter.CanUseGraphVideoSourceDirectly(512, 384, scene));
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
