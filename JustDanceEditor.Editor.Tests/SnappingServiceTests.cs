using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class SnappingServiceTests
{
    [Fact]
    public void SnapToPlayhead_HasHighestPriority()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService())
        {
            SnapToCurrentTimeMarker = true,
            CurrentBeat = 5.0
        };

        double res = SnappingService.FindSnapBeat(5.05, timeline);
        Assert.Equal(5.0, res, 6);
    }

    [Fact]
    public void SnapToGrid_Works()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService())
        {
            SnapToGrid = true,
            SnapGridSize = 1.0
        };

        double r = SnappingService.FindSnapBeat(3.95, timeline);
        Assert.Equal(4.0, r, 6);
    }

    [Fact]
    public void SnapToClips_ExcludesSpecifiedClips()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService())
        {
            SnapToClips = true
        };

        TrackViewModel track = new();
        PictogramClipViewModel c1 = new(new PictogramClip { PictogramId = "a" }, 24, Colors.LightBlue, "a", "", timeline)
        {
            StartBeat = 2.0
        };
        track.Clips.Add(c1);
        timeline.Tracks.Add(track);

        double resExclude = SnappingService.FindSnapBeat(2.1, timeline, new[] { c1 });
        Assert.Equal(2.1, resExclude);

        double resInclude = SnappingService.FindSnapBeat(2.1, timeline);
        Assert.Equal(2.0, resInclude);
    }
}