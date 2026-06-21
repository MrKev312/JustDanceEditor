using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Intermediate;
using JustDanceEditor.Formats.UbiArt.Model;

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
    public void SongStartTime_UsesVideoStartTimeForPositiveStartBeat()
    {
        JDUbiArtSong song = new()
        {
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Structure = new Structure
                            {
                                StartBeat = 2,
                                VideoStartTime = -1.132f,
                                Markers = [0, 27168, 54336]
                            }
                        }
                    }
                ]
            }
        };

        Assert.Equal(1.132f, song.GetSongStartTime(), precision: 6);
    }

    [Fact]
    public void SongStartTime_UsesVideoStartTimeForNegativeStartBeat()
    {
        JDUbiArtSong song = new()
        {
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Structure = new Structure
                            {
                                StartBeat = -9,
                                VideoStartTime = -0.967f,
                                Markers = [0, 24000, 48000, 72000, 96000, 120000, 144000, 168000, 192000, 210911]
                            }
                        }
                    }
                ]
            }
        };

        Assert.Equal(0.967f, song.GetSongStartTime(), precision: 6);
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
