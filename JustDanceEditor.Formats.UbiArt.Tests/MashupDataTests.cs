using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Timeline;

using SixLabors.ImageSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class MashupDataTests
{
    [Fact]
    public void MashupVideoConsecutiveCheckUsesBlockNameBeatSuffixAndDuration()
    {
        LegacyMashupBlockDescriptor previous = new()
        {
            SongName = "source_48",
            FirstBeat = 48,
            LastBeat = 64
        };
        LegacyMashupBlockDescriptor consecutive = new()
        {
            SongName = "other_64",
            FirstBeat = 64,
            LastBeat = 80
        };
        LegacyMashupBlockDescriptor gapped = new()
        {
            SongName = "other_72",
            FirstBeat = 72,
            LastBeat = 88
        };

        Assert.True(MashupTiming.IsConsecutiveBlock(previous, consecutive));
        Assert.False(MashupTiming.IsConsecutiveBlock(previous, gapped));
        Assert.False(MashupTiming.IsConsecutiveBlock(null, consecutive));
    }


    [Fact]
    public void MashupVideoTimingUsesBlockflowBeatWithoutVideoOffset()
    {
        TimelineStructureDocument timeline = new()
        {
            StartBeat = 1,
            VideoStartOffset = -0.5,
            Markers = [0, 48000, 96000, 144000, 192000]
        };

        Assert.Equal(48, MashupTiming.GetLocalTapeFrame(timeline, absoluteBeat: 2));
        AssertClose(2.0, MashupTiming.GetOutputSeconds(timeline, absoluteBeat: 2));
    }


    [Fact]
    public void MashupRenderTimelineDropsNegativePreroll()
    {
        TimelineStructureDocument timeline = new()
        {
            StartBeat = -9,
            EndBeat = 446,
            VideoStartOffset = -4.5,
            Markers = Enumerable.Range(0, 447).Select(i => i * 24000).ToList()
        };

        TimelineStructureDocument renderTimeline = MashupTiming.CreateRenderTimeline(timeline);

        Assert.Equal(0, renderTimeline.StartBeat);
        AssertClose(0, renderTimeline.VideoStartOffset);
        Assert.Equal(timeline.EndBeat, renderTimeline.EndBeat);
        AssertClose(0.0, MashupTiming.GetOutputSeconds(renderTimeline, absoluteBeat: 0));
        AssertClose(1.5, MashupTiming.GetOutputSeconds(renderTimeline, absoluteBeat: 3));
        AssertClose(223.0, MashupTiming.GetOutputSeconds(renderTimeline, absoluteBeat: 446));
    }


    [Fact]
    public void MashupTransitionVisitsScheduleInitialAndPerCoachColorStates()
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
                    AbsoluteStartBeat = 0,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "BlurredLines", FirstBeat = 0, LastBeat = 7 }
                },
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
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 39,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "BlurredLines", FirstBeat = 0, LastBeat = 5 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            transitionTapeLeadFrames: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["coach_move_1.tape"] = 10,
                ["coach_move_2.tape"] = 10
            });

        Assert.Equal(6, visits.Count);
        Assert.EndsWith(Path.Combine("cinematics", "init.tape"), visits[0].Path);
        Assert.Equal(0, visits[0].TimeOffsetFrames);
        Assert.Null(visits[0].DurationFrames);
        Assert.True(visits[0].PersistentMaterialState);
        Assert.EndsWith(Path.Combine("cinematics", "color_green.tape"), visits[1].Path);
        Assert.Equal(0, visits[1].TimeOffsetFrames);
        Assert.Equal(168, visits[1].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "color_purple.tape"), visits[2].Path);
        Assert.Equal(168, visits[2].TimeOffsetFrames);
        Assert.Equal(384, visits[2].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "coach_move_1.tape"), visits[3].Path);
        Assert.Equal(158, visits[3].TimeOffsetFrames);
        Assert.Null(visits[3].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "color_blue.tape"), visits[4].Path);
        Assert.Equal(552, visits[4].TimeOffsetFrames);
        Assert.Equal(504, visits[4].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "coach_move_2.tape"), visits[5].Path);
        Assert.Equal(542, visits[5].TimeOffsetFrames);
        Assert.Null(visits[5].DurationFrames);

        IReadOnlyList<TapeVisit> fxVisits = MashupTransitionFxScheduler.BuildTransitionFxTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            fxTapeLeadFrames: 10);

        Assert.Equal(4, fxVisits.Count);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[0].Path);
        Assert.Equal(152, fxVisits[0].TimeOffsetFrames);
        Assert.Null(fxVisits[0].DurationFrames);
        Assert.Contains("x_lines_2x5", fxVisits[0].TargetFilter!.IncludeKeyContains);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[1].Path);
        Assert.Equal(152, fxVisits[1].TimeOffsetFrames);
        Assert.Null(fxVisits[1].DurationFrames);
        Assert.Contains("x_lines_2x5", fxVisits[1].TargetFilter!.ExcludeKeyContains);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[2].Path);
        Assert.Equal(536, fxVisits[2].TimeOffsetFrames);
        Assert.Null(fxVisits[2].DurationFrames);
        Assert.Contains("x_lines_2x5", fxVisits[2].TargetFilter!.IncludeKeyContains);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[3].Path);
        Assert.Equal(536, fxVisits[3].TimeOffsetFrames);
        Assert.Null(fxVisits[3].DurationFrames);
        Assert.Contains("x_lines_2x5", fxVisits[3].TargetFilter!.ExcludeKeyContains);
    }


    [Fact]
    public void MashupTransitionVisitsSkipConsecutiveLegacyBlocks()
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
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "CrazyInLove_0", FirstBeat = 0, LastBeat = 16 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 23,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "DifferentSong_16", FirstBeat = 0, LastBeat = 16 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 39,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "BlurredLines_40", FirstBeat = 0, LastBeat = 16 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            transitionTapeLeadFrames: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["coach_move_1.tape"] = 10,
                ["coach_move_2.tape"] = 10
            });
        IReadOnlyList<TapeVisit> fxVisits = MashupTransitionFxScheduler.BuildTransitionFxTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            fxTapeLeadFrames: 10);

        int[] coachMoveOffsets =
        [
            .. visits
                .Where(visit => Path.GetFileName(visit.Path).StartsWith("coach_move_", StringComparison.OrdinalIgnoreCase))
                .Select(visit => visit.TimeOffsetFrames)
        ];
        Assert.Equal(new[] { 158, 926 }, coachMoveOffsets);
        Assert.DoesNotContain(visits, visit => visit.TimeOffsetFrames == 542);

        Assert.Equal(4, fxVisits.Count);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[0].Path);
        Assert.Equal(152, fxVisits[0].TimeOffsetFrames);
        Assert.Null(fxVisits[0].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[1].Path);
        Assert.Equal(152, fxVisits[1].TimeOffsetFrames);
        Assert.Null(fxVisits[1].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[2].Path);
        Assert.Equal(920, fxVisits[2].TimeOffsetFrames);
        Assert.Null(fxVisits[2].DurationFrames);
        Assert.EndsWith(Path.Combine("cinematics", "fx.tape"), fxVisits[3].Path);
        Assert.Equal(920, fxVisits[3].TimeOffsetFrames);
        Assert.Null(fxVisits[3].DurationFrames);
    }


    [Fact]
    public void MashupTransitionFxVisitsDoNotSynthesizeFinalCoachFlash()
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
                    AbsoluteStartBeat = 4,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "CrazyInLove_84", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 12,
                    UsesAlternativeBlock = false,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Base", FirstBeat = 0, LastBeat = 20 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> fxVisits = MashupTransitionFxScheduler.BuildTransitionFxTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            fxTapeLeadFrames: 10);

        Assert.Equal(2, fxVisits.Count);
        Assert.Equal(80, fxVisits[0].TimeOffsetFrames);
        Assert.Equal(80, fxVisits[1].TimeOffsetFrames);
        Assert.Contains("x_lines_2x5", fxVisits[0].TargetFilter!.IncludeKeyContains);
        Assert.Contains("x_lines_2x5", fxVisits[1].TargetFilter!.ExcludeKeyContains);
    }


    [Fact]
    public void MashupTransitionVisitsUseAuthoredColorTapeSequence()
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
                    AbsoluteStartBeat = 4,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "CrazyInLove_84", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 12,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "WhereHaveYouALT", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 20,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "BlurredLinesALT", FirstBeat = 0, LastBeat = 8 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            transitionColorTapeNames: ["color_purple.tape", "color_green.tape"],
            initialColorTapeName: "color_orange.tape");

        string[] colorTapeNames =
        [
            .. visits
                .Select(visit => Path.GetFileName(visit.Path))
                .Where(name => name.StartsWith("color_", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.Equal(
            ["color_orange.tape", "color_purple.tape", "color_green.tape", "color_purple.tape"],
            colorTapeNames);
        Assert.DoesNotContain(colorTapeNames, name => string.Equals(name, "color_blue.tape", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void MashupTransitionVisitsPreserveAuthoredAllColorTapeOrder()
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
                    AbsoluteStartBeat = 4,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "First", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 12,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Second", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 20,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Third", FirstBeat = 0, LastBeat = 8 }
                },
                new LegacyMashupBlock
                {
                    AbsoluteStartBeat = 28,
                    UsesAlternativeBlock = true,
                    SourceBlock = new LegacyMashupBlockDescriptor { SongName = "Fourth", FirstBeat = 0, LastBeat = 8 }
                }
            ]
        };

        IReadOnlyList<TapeVisit> visits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(
            Path.Combine("world", "jd5", "_mashup", "cinematics"),
            mashup,
            timeline,
            UbiArtEngineVersion.JD2014,
            transitionColorTapeNames: ["color_green.tape", "color_orange.tape", "color_blue.tape", "color_purple.tape"],
            initialColorTapeName: "color_purple.tape");

        string[] colorTapeNames =
        [
            .. visits
                .Select(visit => Path.GetFileName(visit.Path))
                .Where(name => name.StartsWith("color_", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.Equal(
            ["color_purple.tape", "color_green.tape", "color_orange.tape", "color_blue.tape", "color_purple.tape"],
            colorTapeNames);
    }


    [Fact]
    public void MashupNumberedBlockNamesCanFallBackToBaseMapVideos()
    {
        Assert.True(MashupSourceVideoResolver.TryGetNumericBlockBaseSongName("Diamonds_0", out string? diamondsBaseSongName));
        Assert.Equal("Diamonds", diamondsBaseSongName);

        Assert.True(MashupSourceVideoResolver.TryGetNumericBlockBaseSongName("Wild_Wild_West_178", out string? underscoredBaseSongName));
        Assert.Equal("Wild_Wild_West", underscoredBaseSongName);

        Assert.False(MashupSourceVideoResolver.TryGetNumericBlockBaseSongName("KissKiss", out _));
        Assert.False(MashupSourceVideoResolver.TryGetNumericBlockBaseSongName("Song_ALT", out _));
    }


    [Fact]
    public void MashupPleoUvOverrideMatchesLegacyOutputTargetModifier()
    {
        CinematicUvRect identity = MashupPleoTrackBuilder.CreateUvOverride(0, 0, 1);
        AssertClose(0, identity.Left);
        AssertClose(0, identity.Top);
        AssertClose(1, identity.Right);
        AssertClose(1, identity.Bottom);

        double offsetX = -0.083333;
        double offsetY = -0.0703125;
        double scale = 1.1128;
        double inverseScale = 1.0 / scale;
        double renderQuadRatio = 16.0 / 9.0;
        double scaleOffsetU = 0.5 - (0.5 / renderQuadRatio);
        double translationU = (-offsetX / renderQuadRatio) / scale;
        double translationV = -offsetY / scale;

        CinematicUvRect uv = MashupPleoTrackBuilder.CreateUvOverride(offsetX, offsetY, scale);

        AssertClose((scaleOffsetU * (1.0 - inverseScale)) + translationU, uv.Left);
        AssertClose(translationV, uv.Top);
        AssertClose(inverseScale + (scaleOffsetU * (1.0 - inverseScale)) + translationU, uv.Right);
        AssertClose(inverseScale + translationV, uv.Bottom);
    }


}