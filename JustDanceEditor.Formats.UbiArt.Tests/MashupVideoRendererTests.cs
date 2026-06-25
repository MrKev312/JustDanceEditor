using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTimelineTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class MashupVideoRendererTests
{
    [Fact]
    public void MashupSceneActorFilterKeepsAuthoredGodrayScreenOverlay()
    {
        CinematicActor godrayScreen = CreateActor(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_mashup_godrayscreen"],
            z: 2.0f,
            texturePath: "godrayscreen.png");
        CinematicActor flashCoach = CreateActor(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_flashcoach"],
            z: 1.0f,
            texturePath: "flashcoach.png");
        HashSet<string> transitionKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            godrayScreen.Key,
            flashCoach.Key
        };

        Assert.True(MashupSceneActorFilter.ShouldRenderSceneActor(godrayScreen, transitionKeys));
        Assert.True(MashupSceneActorFilter.ShouldRenderSceneActor(flashCoach, transitionKeys));
    }


    [Fact]
    public void MashupStackedAlphaDetectorDoesNotSplitFullHdPleoSource()
    {
        Assert.False(MashupSegmentBuilder.IsLikelyStackedAlphaVideo(1920, 1080));
        Assert.True(MashupSegmentBuilder.IsLikelyStackedAlphaVideo(1280, 1080));
        Assert.False(MashupSegmentBuilder.IsLikelyStackedAlphaVideo(1920, 720));
    }


    [Fact]
    public void MashupCoachPlacementUsesCookedCenterCoachDepthWithoutHidingCarouselActors()
    {
        CinematicActor coachSubScene = CreateActor(
            ["_mashup_graph", "coachs"],
            typeId: LegacyBinarySerializer.GetTypeId<CinematicSubSceneActorBinary>(),
            z: -2.0f,
            scenePriority: 4,
            sourceOffset: 12);
        CinematicActor centerCoach = CreateActor(
            ["_mashup_graph", "coachs", "circle03_coach06_center"],
            z: 0.75f,
            scenePriority: 7,
            sourceOffset: 3456);
        CinematicActor unrelatedCenter = CreateActor(
            ["_mashup_graph", "coachs", "circle01_coach01_center"],
            z: 99.0f,
            scenePriority: 1,
            sourceOffset: 1);

        MashupCoachLayerPlacement placement =
            MashupSceneActorFilter.ResolveCoachLayerPlacement(
                new CinematicScene([coachSubScene, unrelatedCenter, centerCoach]),
                NullLogger.Instance);

        Assert.Equal(-1.25f, placement.Depth, precision: 4);
        Assert.Equal(7, placement.ScenePriority);
        Assert.Equal(3456, placement.PrimitiveTieBreak);
        Assert.True(MashupSceneActorFilter.ShouldRenderSceneActor(centerCoach, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        Assert.True(MashupSceneActorFilter.ShouldRenderSceneActor(unrelatedCenter, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }


    [Fact]
    public void MashupSourceSegmentSelectionUsesAlternativeBlockScenesOnly()
    {
        LegacyMashupBlock leadingBaseBlock = new()
        {
            AbsoluteStartBeat = 0,
            UsesAlternativeBlock = false,
            SourceBlock = new LegacyMashupBlockDescriptor
            {
                SongName = "FeelSoRight",
                FirstBeat = 0,
                LastBeat = 36
            }
        };
        LegacyMashupBlock alternativeBlock = new()
        {
            AbsoluteStartBeat = 36,
            UsesAlternativeBlock = true,
            SourceBlock = new LegacyMashupBlockDescriptor
            {
                SongName = "IWillSurvive",
                FirstBeat = 327,
                LastBeat = 343
            }
        };
        LegacyMashupBlock emptyBlock = new()
        {
            AbsoluteStartBeat = 52,
            UsesAlternativeBlock = true,
            SourceBlock = new LegacyMashupBlockDescriptor
            {
                FirstBeat = 52,
                LastBeat = 68,
                IsEmptyBlock = true
            }
        };

        Assert.False(MashupSegmentBuilder.ShouldRenderSourceSegment(leadingBaseBlock));
        Assert.False(MashupSegmentBuilder.ShouldRunTransition(leadingBaseBlock));
        Assert.True(MashupSegmentBuilder.ShouldRenderSourceSegment(alternativeBlock));
        Assert.True(MashupSegmentBuilder.ShouldRunTransition(alternativeBlock));
        Assert.False(MashupSegmentBuilder.ShouldRenderSourceSegment(emptyBlock));
    }


    [Fact]
    public void MashupVideoFadePolicyMatchesJd2014VideoFadeBrickWindows()
    {
        TimelineStructureDocument timeline = new()
        {
            Markers = Enumerable.Range(0, 32).Select(index => index * 48000).ToList()
        };
        LegacyMashupData mashup = new()
        {
            Blocks =
            [
                new LegacyMashupBlock
                {
                    Index = 0,
                    AbsoluteStartBeat = 0,
                    UsesAlternativeBlock = false,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Base", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    Index = 1,
                    AbsoluteStartBeat = 8,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Crazy_8", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    Index = 2,
                    AbsoluteStartBeat = 16,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Other_20", FirstBeat = 0, LastBeat = 8 }
                }
            ]
        };

        List<MaterializedMashupSegment> segments =
        [
            CreateMashupSegment(blockIndex: 1, absoluteStartBeat: 8, sourceSongName: "Crazy_8", outputStartSeconds: 8, outputDurationSeconds: 8),
            CreateMashupSegment(blockIndex: 2, absoluteStartBeat: 16, sourceSongName: "Other_20", outputStartSeconds: 16, outputDurationSeconds: 8)
        ];

        IReadOnlyList<MaterializedMashupSegment> faded =
            MashupSegmentBuilder.ApplyVideoFadePolicy(mashup, timeline, segments);

        AssertClose(0.25, faded[0].FadeInDurationSeconds);
        AssertClose(0, faded[0].FadeInDelaySeconds);
        AssertClose(0.25, faded[0].FadeOutDurationSeconds);
        AssertClose(0.25, faded[1].FadeInDurationSeconds);
        AssertClose(0, faded[1].FadeInDelaySeconds);
        AssertClose(0, faded[1].FadeOutDurationSeconds);
    }


    [Fact]
    public void MashupVideoFadeAlphaUsesPleoOutputTargetFadeWindows()
    {
        AssertClose(0, CinematicExternalPleoTrack.ComputeFadeAlpha(0.0, 4.0, 0.25, 0.25));
        AssertClose(0.5, CinematicExternalPleoTrack.ComputeFadeAlpha(0.125, 4.0, 0.25, 0.25));
        AssertClose(1, CinematicExternalPleoTrack.ComputeFadeAlpha(2.0, 4.0, 0.25, 0.25));
        AssertClose(0.5, CinematicExternalPleoTrack.ComputeFadeAlpha(3.875, 4.0, 0.25, 0.25));
        AssertClose(0, CinematicExternalPleoTrack.ComputeFadeAlpha(4.0, 4.0, 0.25, 0.25));
    }


    [Fact]
    public void MashupVideoFadeAlphaSupportsExplicitFadeInDelay()
    {
        AssertClose(0, CinematicExternalPleoTrack.ComputeFadeAlpha(0.0, 4.0, 0.25, 0.0, fadeInDelaySeconds: 0.25));
        AssertClose(0, CinematicExternalPleoTrack.ComputeFadeAlpha(0.25, 4.0, 0.25, 0.0, fadeInDelaySeconds: 0.25));
        AssertClose(0.5, CinematicExternalPleoTrack.ComputeFadeAlpha(0.375, 4.0, 0.25, 0.0, fadeInDelaySeconds: 0.25));
        AssertClose(1, CinematicExternalPleoTrack.ComputeFadeAlpha(0.5, 4.0, 0.25, 0.0, fadeInDelaySeconds: 0.25));
    }


    [Fact]
    public void MashupCoachRevealDelayUsesGodrayScreenAlphaClipEnd()
    {
        uint alphaTypeId = LegacyBinarySerializer.GetTypeId<CinematicAlphaClipBinary>();
        TapeClip godrayAlpha = new(
            alphaTypeId,
            StartFrame: 10,
            DurationFrames: 21,
            Path: null,
            Targets: [new ActorTargetPath(["_mashup_graph", "fx", "x_speedlines_00", "x_mashup_godrayscreen"])],
            Curves:
            [
                new CinematicCurve(
                [
                    new CinematicKeyframe(0, 0, 0, 0, 0, 0),
                    new CinematicKeyframe(5, 1, 5, 1, 5, 1),
                    new CinematicKeyframe(21, 0, 21, 0, 21, 0)
                ])
            ]);

        Assert.Equal(31, MashupTransitionFxScheduler.GetCoachRevealDelayFrames([godrayAlpha]));
    }


    [Fact]
    public void MashupTransitionFxVisitUsesAuthoredFxTapeLeadAtBlockStart()
    {
        Assert.Equal(
            -16,
            MashupTransitionFxScheduler.GetVisitOffsetFromBlock(
                fxTapeLeadFrames: 10));

        Assert.Equal(
            -16,
            MashupTransitionFxScheduler.GetVisitOffsetFromBlock(
                fxTapeLeadFrames: 10));

        Assert.Equal(
            -32,
            MashupTransitionFxScheduler.GetVisitOffsetFromBlock(
                fxTapeLeadFrames: 10,
                fxTapeFlashLeadFrames: 18));

        Assert.Equal(6, MashupTransitionFxScheduler.GetCoachFadeActiveFrames());
    }

}