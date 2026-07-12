using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public sealed class TimelineZoomTests
{
    [Fact]
    public void OneHundredPercent_FitsEntireTimelineToViewport()
    {
        using TimelineEditorViewModel timeline = CreateTimeline(startBeat: -100, endBeat: 500);

        timeline.UpdateViewportWidth(1200);

        Assert.Equal(100, timeline.ZoomPercentage);
        Assert.Equal(2, timeline.PixelsPerBeat, precision: 6);
        Assert.Equal(1200, timeline.TimelineWidth, precision: 6);
    }

    [Fact]
    public void ZoomPercentage_IsRelativeToFitWidthAndCannotZoomOutFurther()
    {
        using TimelineEditorViewModel timeline = CreateTimeline(startBeat: 0, endBeat: 600);
        timeline.UpdateViewportWidth(1200);

        timeline.ZoomPercentage = 250;

        Assert.Equal(5, timeline.PixelsPerBeat, precision: 6);
        Assert.Equal(3000, timeline.TimelineWidth, precision: 6);

        timeline.ZoomPercentage = 50;

        Assert.Equal(100, timeline.ZoomPercentage);
        Assert.Equal(2, timeline.PixelsPerBeat, precision: 6);
        Assert.Equal(1200, timeline.TimelineWidth, precision: 6);
    }

    [Fact]
    public void BitmapCache_IsLimitedToReasonablySizedOverviewZooms()
    {
        using TimelineEditorViewModel timeline = CreateTimeline(startBeat: 0, endBeat: 600);
        timeline.UpdateViewportWidth(1200);

        Assert.True(timeline.UseTimelineBitmapCache);

        timeline.ZoomPercentage = 200;
        Assert.True(timeline.UseTimelineBitmapCache);

        timeline.ZoomPercentage = 201;
        Assert.False(timeline.UseTimelineBitmapCache);
    }

    private static TimelineEditorViewModel CreateTimeline(int startBeat, int endBeat)
    {
        IntermediateSongPackage package = new()
        {
            TimelineStructure = new TimelineStructureDocument
            {
                StartBeat = startBeat,
                EndBeat = endBeat
            }
        };

        return new TimelineEditorViewModel(
            package,
            "root",
            new PlaybackService(),
            new TimelineSettingsService());
    }
}
