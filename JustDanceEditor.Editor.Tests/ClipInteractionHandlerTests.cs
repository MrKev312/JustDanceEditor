using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Interactions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Reflection;

namespace JustDanceEditor.Editor.Tests;

public class ClipInteractionHandlerTests
{
    private static ClipDragHandler CreateClipDragHandler() => new(null);

    private static ClipResizeHandler CreateClipResizeHandler() => new(null);

    private static void SetPrivateField(object obj, string name, object? value)
    {
        FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(obj.GetType().FullName, name);
        f.SetValue(obj, value);
    }

    private static TimelineEditorViewModel CreateTimelineWithBounds(double startBeat = 0.0, double endBeat = 100.0)
    {
        IntermediateSongPackage package = new()
        {
            TimelineStructure = new TimelineStructureDocument
            {
                StartBeat = (int)startBeat,
                EndBeat = (int)endBeat
            }
        };
        return new TimelineEditorViewModel(package, "root", new PlaybackService(), new TimelineSettingsService());
    }

    [Fact]
    public void ClipViewModel_IsResizableFlag_CorrectlySet()
    {
        TimelineEditorViewModel timeline = new(new IntermediateSongPackage(), "root", new PlaybackService(), new TimelineSettingsService());
        PictogramClipViewModel p = new(new PictogramClip { Duration = 24 }, "", timeline);
        KaraokeClipViewModel k = new(new KaraokeClip { Duration = 24 }, "", timeline);
        MoveClipViewModel m = new(new MoveClip(), "", timeline, fallbackDurationFrames: 24);
        HideUserInterfaceClipViewModel h = new(new HideUserInterfaceClip { Duration = 24 }, "", timeline);
        GoldEffectClipViewModel g = new(new GoldEffectClip { Duration = 24 }, "", timeline);
        VideoClipViewModel v = new(8.0, "", timeline);

        Assert.True(p.IsResizable);
        Assert.True(k.IsResizable);
        Assert.True(m.IsResizable);
        Assert.True(h.IsResizable);
        Assert.False(g.IsResizable);
        Assert.False(v.IsResizable);
    }

    [Fact]
    public void VideoClip_StartBeat_UpdatesTimelineVideoOffset()
    {
        IntermediateSongPackage package = new()
        {
            TimelineStructure = new TimelineStructureDocument
            {
                StartBeat = 0,
                EndBeat = 200,
                VideoStartOffset = 0,
                Markers = [0, 48000, 96000, 144000, 192000, 240000, 288000]
            }
        };

        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());
        VideoClipViewModel clip = new(4.0, "", timeline);

        clip.StartBeat = 2.0;

        Assert.Equal(-2.0, timeline.VideoOffset, 6);
        Assert.Equal(2.0, clip.StartBeat, 6);
        Assert.Equal(2.0, package.TimelineStructure.VideoStartOffset, 6);
    }

    [Fact]
    public void VideoClip_Drag_CanStartBeforeTimelineStart()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);
        VideoClipViewModel clip = new(120.0, "", timeline);

        ClipDragHandler handler = CreateClipDragHandler();
        SetPrivateField(handler, "_isDragging", true);
        SetPrivateField(handler, "_draggingClip", clip);
        SetPrivateField(handler, "_dragStartPointerX", 0.0);
        SetPrivateField(handler, "_dragOriginalStartBeat", clip.StartBeat);

        handler.UpdateDrag(new Avalonia.Point(-10.0 * 24.0, 0), 24.0, timeline);

        Assert.Equal(-10.0, clip.StartBeat, 6);
        Assert.True(clip.StartBeat <= timeline.TimelineStructure.StartBeat);
        Assert.True(clip.StartBeat + clip.DurationBeats >= timeline.TimelineStructure.EndBeat);
    }

    [Fact]
    public void VideoClip_Drag_StopsAtLatestStartThatStillCoversTimelineEnd()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);
        VideoClipViewModel clip = new(120.0, "", timeline);

        ClipDragHandler handler = CreateClipDragHandler();
        SetPrivateField(handler, "_isDragging", true);
        SetPrivateField(handler, "_draggingClip", clip);
        SetPrivateField(handler, "_dragStartPointerX", 0.0);
        SetPrivateField(handler, "_dragOriginalStartBeat", clip.StartBeat);

        handler.UpdateDrag(new Avalonia.Point(50.0 * 24.0, 0), 24.0, timeline);

        Assert.Equal(0.0, clip.StartBeat, 6);
        Assert.True(clip.StartBeat <= timeline.TimelineStructure.StartBeat);
        Assert.True(clip.StartBeat + clip.DurationBeats >= timeline.TimelineStructure.EndBeat);
    }

    [Fact]
    public void SingleDrag_NoUndo_When_NoMovement()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 5.0
        };

        ClipDragHandler handler = CreateClipDragHandler();
        SetPrivateField(handler, "_isDragging", true);
        SetPrivateField(handler, "_dragOriginalStartBeat", 5.0);
        SetPrivateField(handler, "_draggingClip", clip);

        handler.Complete(timeline, null);

        Assert.False(timeline.UndoService.CanUndo);
    }

    [Fact]
    public void SingleDrag_RecordsUndo_When_Moved()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 5.0
        };

        ClipDragHandler handler = CreateClipDragHandler();
        SetPrivateField(handler, "_isDragging", true);
        SetPrivateField(handler, "_dragOriginalStartBeat", 5.0);
        SetPrivateField(handler, "_draggingClip", clip);

        // simulate movement
        clip.StartBeat = 6.5;

        handler.Complete(timeline, null);

        Assert.True(timeline.UndoService.CanUndo);

        // undo restores start
        timeline.UndoService.Undo();
        Assert.Equal(5.0, clip.StartBeat, 6);
    }

    [Fact]
    public void MultiDrag_RecordsUndo_When_Changed()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        PictogramClipViewModel c1 = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline);
        PictogramClipViewModel c2 = new(new PictogramClip { PictogramId = "b", Duration = 24 }, "", timeline);
        c1.StartBeat = 2.0;
        c2.StartBeat = 4.0;

        ClipDragHandler handler = CreateClipDragHandler();

        Dictionary<ClipViewModel, double> dict = new()
        {
            { c1, 2.0 }, { c2, 4.0 }
        };

        SetPrivateField(handler, "_isMultiDragging", true);
        SetPrivateField(handler, "_multiDragOriginalStarts", dict);

        // move both
        c1.StartBeat = 3.0;
        c2.StartBeat = 5.0;

        handler.Complete(timeline, null);

        Assert.True(timeline.UndoService.CanUndo);

        timeline.UndoService.Undo();
        Assert.Equal(2.0, c1.StartBeat, 6);
        Assert.Equal(4.0, c2.StartBeat, 6);
    }

    [Fact]
    public void Resize_NoUndo_When_NoChange()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 1.0,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeOriginalStart", 1.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 2.0);
        SetPrivateField(handler, "_isResizingLeft", true);

        // no actual change
        clip.StartBeat = 1.0;
        clip.DurationBeats = 2.0;

        handler.Complete(timeline, null);

        Assert.False(timeline.UndoService.CanUndo);
    }

    [Fact]
    public void Resize_RecordsUndo_When_Changed()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 1.0,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeOriginalStart", 1.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 2.0);
        SetPrivateField(handler, "_isResizingRight", true);

        // change duration
        clip.DurationBeats = 3.0;

        handler.Complete(timeline, null);

        Assert.True(timeline.UndoService.CanUndo);

        timeline.UndoService.Undo();
        Assert.Equal(2.0, clip.DurationBeats, 6);
    }

    [Fact]
    public void HideHudResize_RecordsUndo_When_Changed()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        HideUserInterfaceClipViewModel clip = new(new HideUserInterfaceClip { Duration = 24 }, "", timeline)
        {
            StartBeat = 1.0,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeOriginalStart", 1.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 2.0);
        SetPrivateField(handler, "_isResizingRight", true);

        // extend
        clip.DurationBeats = 4.0;

        handler.Complete(timeline, null);

        Assert.True(timeline.UndoService.CanUndo);
        timeline.UndoService.Undo();
        Assert.Equal(2.0, clip.DurationBeats, 6);
    }

    [Fact]
    public void LeftResize_ClampingAtMinimum_DoesNotExpand()
    {
        // Test for the bug fix: resizing a clip left at the minimum position
        // should stay in place, not expand rightward
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Clip starts at timeline minimum
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 0.0,  // At minimum (timeline starts at 0)
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", 0.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 2.0);
        SetPrivateField(handler, "_isResizingLeft", true);

        // Simulate left-resize: moving pointer left (negative delta)
        // This should try to move start to -1.0, but it gets clamped to 0.0
        // With the fix, the end stays at 2.0 and nothing should happen
        handler.UpdateResize(new Avalonia.Point(50.0, 0), 50.0, timeline);

        // The clip should NOT have changed - it's already at the minimum
        Assert.Equal(0.0, clip.StartBeat, 6);
        Assert.Equal(2.0, clip.DurationBeats, 6);
    }

    [Fact]
    public void LeftResize_WithinBounds_WorksCorrectly()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Clip with some space before the minimum
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 5.0,
            DurationBeats = 3.0  // Ends at 8.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", 5.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 3.0);
        SetPrivateField(handler, "_isResizingLeft", true);

        // Move left by 1 beat: new start should be 4.0, end stays at 8.0, duration becomes 4.0
        handler.UpdateResize(new Avalonia.Point(50.0, 0), 50.0, timeline);

        Assert.Equal(4.0, clip.StartBeat, 6);
        Assert.Equal(4.0, clip.DurationBeats, 6);
    }

    [Fact]
    public void LeftResize_ExpandsOutOfBounds_GetsClamped()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Clip positioned such that trying to resize left goes below minimum
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 0.5,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", 0.5);
        SetPrivateField(handler, "_resizeOriginalDuration", 2.0);
        SetPrivateField(handler, "_isResizingLeft", true);

        // Try to move left by 2 beats: would be -1.5, but clamped to 0.0
        // End stays at 2.5, so new duration becomes 2.5
        handler.UpdateResize(new Avalonia.Point(0.0, 0), 50.0, timeline);

        Assert.Equal(0.0, clip.StartBeat, 6);
        Assert.True(clip.DurationBeats >= 2.0, "Duration should be clamped end minus start");
    }

    [Fact]
    public void RightResize_ExpandsWithinBounds_Works()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Clip with room to expand right
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 2.0,
            DurationBeats = 2.0  // Ends at 4.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", 2.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 2.0);
        SetPrivateField(handler, "_isResizingRight", true);

        // Move right by 1 beat: end becomes 5.0, duration becomes 3.0
        handler.UpdateResize(new Avalonia.Point(150.0, 0), 50.0, timeline);

        Assert.Equal(2.0, clip.StartBeat, 6);  // Start doesn't change
        Assert.Equal(3.0, clip.DurationBeats, 6);
    }

    [Fact]
    public void RightResize_ContractWithinBounds_Works()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Clip to contract
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 2.0,
            DurationBeats = 4.0
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", 2.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 4.0);
        SetPrivateField(handler, "_isResizingRight", true);

        // Move left (negative delta): shrink by 1 beat
        handler.UpdateResize(new Avalonia.Point(50.0, 0), 50.0, timeline);

        Assert.Equal(2.0, clip.StartBeat, 6);
        Assert.Equal(3.0, clip.DurationBeats, 6);
    }

    [Fact]
    public void LeftResize_RespectMinimumDuration_DoesNotShrinkBelow()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Clip at minimum duration threshold
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = 2.0,
            DurationBeats = 1.0  // Already very short
        };

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", 2.0);
        SetPrivateField(handler, "_resizeOriginalDuration", 1.0);
        SetPrivateField(handler, "_isResizingLeft", true);

        // Try to resize left, which would shrink duration even more
        handler.UpdateResize(new Avalonia.Point(0.0, 0), 50.0, timeline);

        // Duration should stay at original or not go below 0.5 minimum
        Assert.True(clip.DurationBeats >= 0.5, "Duration should respect minimum threshold");
    }

    [Fact]
    public void LeftResize_ClampingAtMaxEnd_KeepsEndInBounds()
    {
        TimelineEditorViewModel timeline = CreateTimelineWithBounds(0.0, 100.0);

        // Setup: clip that when resized left tries to push its end beyond the timeline max
        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a", Duration = 24 }, "", timeline)
        {
            StartBeat = timeline.TimelineStructure.EndBeat - 2.0,  // Near the end
            DurationBeats = 2.0
        };

        double originalEnd = clip.StartBeat + clip.DurationBeats;

        ClipResizeHandler handler = CreateClipResizeHandler();
        SetPrivateField(handler, "_resizingClip", clip);
        SetPrivateField(handler, "_resizeStartPointerX", 100.0);
        SetPrivateField(handler, "_resizeOriginalStart", clip.StartBeat);
        SetPrivateField(handler, "_resizeOriginalDuration", clip.DurationBeats);
        SetPrivateField(handler, "_isResizingLeft", true);

        // Move left to expand the clip; the end should be clamped to max
        handler.UpdateResize(new Avalonia.Point(0.0, 0), 50.0, timeline);

        double newEnd = clip.StartBeat + clip.DurationBeats;
        Assert.True(newEnd <= timeline.TimelineStructure.EndBeat, "Clip end should remain within bounds");
    }
}