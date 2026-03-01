using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Interactions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Reflection;

namespace JustDanceEditor.Editor.Tests;

public class ClipInteractionHandlerTests
{
    private static void SetPrivateField(object obj, string name, object? value)
    {
        FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        f.SetValue(obj, value);
    }

    [Fact]
    public void ClipViewModel_IsResizableFlag_CorrectlySet()
    {
        TimelineEditorViewModel timeline = new(new IntermediateSongPackage(), "root");
        PictogramClipViewModel p = new(new PictogramClip(), 24, Colors.LightBlue, "", "", timeline);
        KaraokeClipViewModel k = new(new KaraokeClip(), 24, Colors.Goldenrod, "", "", timeline);
        MoveClipViewModel m = new(new MoveClip(), 24, Colors.LightGray, "", "", timeline);
        HideUserInterfaceClipViewModel h = new(new HideUserInterfaceClip(), 24, Colors.MediumPurple, string.Empty, "", timeline);
        GoldEffectClipViewModel g = new(new GoldEffectClip(), 24, Colors.Gold, "", "", timeline);

        Assert.True(p.IsResizable);
        Assert.True(k.IsResizable);
        Assert.True(m.IsResizable);
        Assert.True(h.IsResizable);
        Assert.False(g.IsResizable);
    }

    [Fact]
    public void SingleDrag_NoUndo_When_NoMovement()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root");

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a" }, 24, Colors.LightBlue, "a", "", timeline)
        {
            StartBeat = 5.0
        };

        ClipDragHandler handler = new(null!);
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
        TimelineEditorViewModel timeline = new(package, "root");

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a" }, 24, Colors.LightBlue, "a", "", timeline)
        {
            StartBeat = 5.0
        };

        ClipDragHandler handler = new(null!);
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
        TimelineEditorViewModel timeline = new(package, "root");

        PictogramClipViewModel c1 = new(new PictogramClip { PictogramId = "a" }, 24, Colors.LightBlue, "a", "", timeline);
        PictogramClipViewModel c2 = new(new PictogramClip { PictogramId = "b" }, 24, Colors.LightBlue, "b", "", timeline);
        c1.StartBeat = 2.0;
        c2.StartBeat = 4.0;

        ClipDragHandler handler = new(null!);

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
        TimelineEditorViewModel timeline = new(package, "root");

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a" }, 24, Colors.LightBlue, "a", "", timeline)
        {
            StartBeat = 1.0,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = new(null!);
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
        TimelineEditorViewModel timeline = new(package, "root");

        PictogramClipViewModel clip = new(new PictogramClip { PictogramId = "a" }, 24, Colors.LightBlue, "a", "", timeline)
        {
            StartBeat = 1.0,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = new(null!);
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
        TimelineEditorViewModel timeline = new(package, "root");

        HideUserInterfaceClipViewModel clip = new(new HideUserInterfaceClip(), 24, Colors.MediumPurple, string.Empty, "", timeline)
        {
            StartBeat = 1.0,
            DurationBeats = 2.0
        };

        ClipResizeHandler handler = new(null!);
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
}