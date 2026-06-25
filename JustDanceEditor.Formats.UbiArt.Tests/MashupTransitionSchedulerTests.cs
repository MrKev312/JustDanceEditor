using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;

using SixLabors.ImageSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTimelineTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class MashupTransitionSchedulerTests
{
    [Fact]
    public void MashupTransitionVisitsCanCarryResolvedTransitionTapeDurations()
    {
        TimelineStructureDocument timeline = new()
        {
            StartBeat = 0,
            Markers = [0, 48000]
        };
        LegacyMashupData mashup = new()
        {
            Blocks =
            [
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 7,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "CrazyInLove_84", FirstBeat = 0, LastBeat = 16 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 23,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "WhereHaveYouALT", FirstBeat = 0, LastBeat = 16 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["coach_move_1.tape"] = 96
            },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["coach_move_1.tape"] = 10
            });
        IReadOnlyList<TapeVisit> fxVisits = MashupTransitionFxScheduler.BuildTransitionFxTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            fxTapeDurationFrames: 72,
            fxTapeLeadFrames: 10);

        TapeVisit coachMoveVisit = Assert.Single(
            visits,
            visit => visit.Path.EndsWith("coach_move_1.tape", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(96, coachMoveVisit.DurationFrames);
        Assert.Equal(4, fxVisits.Count);
        Assert.All(fxVisits, visit => Assert.Equal(72, visit.DurationFrames));
        Assert.Equal(152, fxVisits[0].TimeOffsetFrames);
        Assert.Equal(152, fxVisits[1].TimeOffsetFrames);
        Assert.Equal(536, fxVisits[2].TimeOffsetFrames);
        Assert.Equal(536, fxVisits[3].TimeOffsetFrames);
    }


    [Fact]
    public void MashupTransitionVisitsUseJd2015PulseTapeWithoutFxOverlay()
    {
        TimelineStructureDocument timeline = new()
        {
            Markers = Enumerable.Range(0, 80).Select(index => index * 24000).ToList()
        };
        LegacyMashupData mashup = new()
        {
            Blocks =
            [
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 7,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "CrazyInLove_84", FirstBeat = 0, LastBeat = 16 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 23,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "WhereHaveYouALT", FirstBeat = 0, LastBeat = 16 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd2015", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2015);
        IReadOnlyList<TapeVisit> fxVisits = MashupTransitionFxScheduler.BuildTransitionFxTapeVisits(
            Path.Combine("world", "jd2015", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2015);

        Assert.Empty(fxVisits);
        Assert.DoesNotContain(visits, visit => visit.Path.Contains("coach_move_", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, visits.Count(visit => visit.Path.EndsWith(Path.Combine("cinematics", "pulse_fg.tape"), StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(visits, visit => visit.Path.EndsWith(Path.Combine("cinematics", "color_blue.tape"), StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void MashupTransitionVisitsScheduleJd2015UvScrollStates()
    {
        TimelineStructureDocument timeline = new()
        {
            Markers = Enumerable.Range(0, 80).Select(index => index * 24000).ToList()
        };
        LegacyMashupData mashup = new()
        {
            Blocks =
            [
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 0,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "First", FirstBeat = 0, LastBeat = 21 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 21,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Second", FirstBeat = 0, LastBeat = 16 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 37,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Third", FirstBeat = 0, LastBeat = 16 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd2015", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2015,
            uvScrollTapeNames: ["uv_right.tape", "uv_left.tape"],
            uvScrollTapeDurationFrames: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["uv_right.tape"] = 9999,
                ["uv_left.tape"] = 9999
            });

        TapeVisit[] uvVisits =
        [
            .. visits
                .Where(visit => Path.GetFileName(visit.Path).StartsWith("uv_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(visit => visit.TimeOffsetFrames)
        ];

        Assert.Equal(3, uvVisits.Length);
        Assert.EndsWith(Path.Combine("cinematics", "uv_right.tape"), uvVisits[0].Path);
        Assert.Equal(0, uvVisits[0].TimeOffsetFrames);
        Assert.Equal(504, uvVisits[0].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "uv_left.tape"), uvVisits[1].Path);
        Assert.Equal(504, uvVisits[1].TimeOffsetFrames);
        Assert.Equal(384, uvVisits[1].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "uv_right.tape"), uvVisits[2].Path);
        Assert.Equal(888, uvVisits[2].TimeOffsetFrames);
        Assert.Equal(384, uvVisits[2].DurationFrames);
    }


    [Fact]
    public void MashupTransitionOverlaySelectionUsesAllCookedFxTapeTargets()
    {
        const uint flashFxNameId = 0x6A7D90A4;
        const uint linesFxNameId = 0x0BD862EC;
        TapeClip flashClip = CreateFxClip(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_flashcoach"],
            flashFxNameId);
        TapeClip linesClip = CreateFxClip(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_lines_2x5_00"],
            linesFxNameId);
        TapeClip godrayScreenClip = CreateProportionClip(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_mashup_godrayscreen"]);

        IReadOnlySet<string> transitionKeys = MashupTransitionFxScheduler.BuildTransitionFxActorKeys(
            [flashClip, linesClip, godrayScreenClip]);
        IReadOnlySet<uint> transitionFxNameIds = MashupTransitionFxScheduler.BuildTransitionFxNameIds(
            [flashClip, linesClip, godrayScreenClip]);

        Assert.Contains("_mashup_graph/fx/x_speedlines_00/x_flashcoach", transitionKeys);
        Assert.Contains("_mashup_graph/fx/x_speedlines_00/x_lines_2x5_00", transitionKeys);
        Assert.Contains("_mashup_graph/fx/x_speedlines_00/x_mashup_godrayscreen", transitionKeys);
        Assert.Contains(flashFxNameId, transitionFxNameIds);
        Assert.Contains(linesFxNameId, transitionFxNameIds);

        CinematicActor flashActor = CreateActor(["_mashup_graph", "fx", "x_speedlines_00", "x_flashcoach"]) with
        {
            FxTemplate = CreateFxTemplate(
                flashFxNameId,
                (0x8E990455u, "x_mashup_godray_coach"),
                (0x2CEC2CABu, "x_mashup_godray_coach_explo"))
        };
        CinematicActor linesActor = CreateActor(["_mashup_graph", "fx", "x_speedlines_00", "x_lines_2x5_00"]) with
        {
            FxTemplate = CreateFxTemplate(linesFxNameId, (0x0BD862ECu, "x_mashup_lines_2x5"))
        };

        Assert.True(MashupSceneActorFilter.IsCoachFlashFxActor(flashActor, transitionFxNameIds));
        Assert.False(MashupSceneActorFilter.IsCoachFlashFxActor(linesActor, transitionFxNameIds));
        Assert.False(MashupSceneActorFilter.IsCoachFlashFxActor(flashActor, new HashSet<uint> { linesFxNameId }));
        Assert.True(MashupSceneActorFilter.IsAuthoredTransitionFxActor(flashActor));
        Assert.False(MashupSceneActorFilter.IsAuthoredTransitionFxActor(linesActor));
        Assert.True(MashupSceneActorFilter.IsBackgroundActorKey("_mashup_graph/coachs/circle03_coach06_center"));
        Assert.True(MashupSceneActorFilter.IsBackgroundActorKey("_mashup_graph/coachs/circle01_coach01_center"));
        Assert.True(MashupSceneActorFilter.IsCoachPlacementActorKey("_mashup_graph/coachs/circle03_coach06_center"));
        Assert.False(MashupSceneActorFilter.IsCoachPlacementActorKey("_mashup_graph/coachs/circle01_coach01_center"));
        Assert.True(MashupSceneActorFilter.IsBackgroundActorKey("_mashup_graph/3d_elements/x_disc_ball01"));
    }

    [Fact]
    public void MashupSharedSceneValidationRequiresAuthoredMashupGraphActors()
    {
        CinematicActor discoBall = CreateActor(["_mashup_graph", "3d_elements", "x_disc_ball01"]);
        MashupSceneActorFilter.ValidateSharedScene(new CinematicScene([discoBall]), "_mashup", "BlameItMU");

        CinematicActor runtimeCamera = CreateActor(["Camera_JD"]);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            MashupSceneActorFilter.ValidateSharedScene(new CinematicScene([runtimeCamera]), "_mashup", "BlameItMU"));
        Assert.Contains("_mashup_graph", error.Message);
        Assert.Contains("Refusing to render over the original map background", error.Message);
    }


    [Fact]
    public void MashupTransitionOverlayPlaneDrawsAfterRendererCompositedPleo()
    {
        CinematicActor flash = CreateActor(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_flashcoach"],
            z: -1.0f,
            texturePath: "flash.png");
        RenderableCinematicActor flashRenderable = new(
            flash,
            Image: null,
            RenderGeometry.NoAtlasQuad,
            CinematicLayerPlane.Background,
            -100,
            flash.RelativeZ,
            flash.SourceOffset);
        RenderableCinematicActor mappedFlash = MashupSceneActorFilter.ApplyTransitionOverlayPlane(
            flashRenderable,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { flash.Key });

        CinematicActor pleo = CreateActor(["_mashup_graph", "renderer_coaches", "coach_00"], z: 0);
        RenderableCinematicActor pleoRenderable = new(
            pleo,
            Image: null,
            CinematicGeometryProjector.CreatePleoVideoGeometry(),
            CinematicLayerPlane.Composite,
            100,
            pleo.RelativeZ,
            pleo.SourceOffset,
            CinematicRenderKind.PleoVideo);
        ResolvedActorState flashState = new(0, 0, flash.RelativeZ, 1, 1, 0, 1, RgbTint.White, false);
        ResolvedActorState pleoState = new(0, 0, pleo.RelativeZ, 1, 1, 0, 1, RgbTint.White, false);
        List<FrameDrawItem> items =
        [
            new(mappedFlash, flashState, ProjectedQuad.Empty),
            new(pleoRenderable, pleoState, ProjectedQuad.Empty)
        ];

        items.Sort(CinematicFrameDrawSorter.CompareFrameDrawItems);

        Assert.Equal(CinematicLayerPlane.TransitionOverlay, mappedFlash.Plane);
        Assert.Equal(pleo.Key, items[0].Actor.Actor.Key);
        Assert.Equal(flash.Key, items[1].Actor.Actor.Key);
        Assert.False(CinematicCpuDraw.ShouldUseInjectedDepthTest(items[1], RenderGeometry.NoAtlasQuad));

        RenderGeometry meshAtlas = RenderGeometry.FromAabb(-1, -1, 1, 1, CinematicGeometrySource.MeshAtlas);
        using MaterializedCinematicImage flashImage = CreateSolidImage(flash.Key, flash.TexturePath!, CreateColor(255, 255, 255));
        FrameDrawItem overlayMeshItem = new(
            mappedFlash with
            {
                Image = flashImage,
                Geometry = meshAtlas
            },
            flashState,
            CreateScreenQuad(8, 8));
        List<CinematicGpuDrawItem> gpuItems = [];

        bool added = CinematicGpuDrawBuilder.TryAddGpuDrawItemsForFrameItem(
            new CanvasBuffer(8, 8),
            overlayMeshItem,
            pleoFrame: null,
            materialElapsedSeconds: 0,
            gpuItems,
            out string unsupportedReason);

        CinematicGpuDrawItem gpuItem = Assert.Single(gpuItems);
        Assert.True(added, unsupportedReason);
        Assert.False(CinematicCpuDraw.ShouldUseInjectedDepthTest(overlayMeshItem, meshAtlas));
        Assert.False(gpuItem.DepthTestInjected);
        Assert.Equal(0.0f, gpuItem.InjectedInverseDepth);
    }


    [Fact]
    public void MashupTransitionOverlayPlaneClassifiesAuthoredFlashWhenTapeTargetsMissIt()
    {
        CinematicActor flash = CreateActor(
            ["_mashup_graph", "fx", "x_speedlines_00", "x_flashcoach"],
            z: -1.0f,
            texturePath: "flash.png");
        RenderableCinematicActor flashRenderable = new(
            flash,
            Image: null,
            RenderGeometry.NoAtlasQuad,
            CinematicLayerPlane.Background,
            -100,
            flash.RelativeZ,
            flash.SourceOffset);

        RenderableCinematicActor mappedFlash = MashupSceneActorFilter.ApplyTransitionOverlayPlane(
            flashRenderable,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(CinematicLayerPlane.TransitionOverlay, mappedFlash.Plane);
        Assert.True(MashupSceneActorFilter.ShouldRenderSceneActor(flash, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }


}