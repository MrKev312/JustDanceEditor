using Avalonia.Headless.XUnit;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Reflection;

namespace JustDanceEditor.Editor.Tests;

public sealed class SkiaPictogramPreviewControlTests
{
    [AvaloniaFact]
    public void ClampClipStartBeat_AllowsNegativeTimingWithinTimeline()
    {
        IntermediateSongPackage package = new();
        package.TimelineStructure.StartBeat = -8;
        package.TimelineStructure.EndBeat = 64;
        TimelineEditorViewModel timeline = new(
            package,
            "root",
            new PlaybackService(),
            new TimelineSettingsService());
        PictogramClipViewModel clip = new(
            new PictogramClip { PictogramId = "picto", Duration = 24 },
            "",
            timeline);

        double startBeat = SkiaPictogramPreviewControl.ClampClipStartBeat(-4, clip, timeline);

        Assert.Equal(-4, startBeat, 6);
    }

    [AvaloniaFact]
    public void CompleteDrag_RecordsStartBeatChangeForUndoAndRedo()
    {
        TimelineEditorViewModel timeline = new(
            new IntermediateSongPackage(),
            "root",
            new PlaybackService(),
            new TimelineSettingsService());
        PictogramClipViewModel clip = new(
            new PictogramClip { PictogramId = "picto", Duration = 24 },
            "",
            timeline)
        {
            StartBeat = 5
        };
        SkiaPictogramPreviewControl control = new()
        {
            ActiveTimeline = timeline
        };

        SetField(control, "_draggingClip", clip);
        SetField(control, "_dragOriginalStartBeat", 5d);
        clip.StartBeat = 6.5;

        InvokeCompleteDrag(control);

        Assert.True(timeline.UndoService.CanUndo);
        timeline.UndoService.Undo();
        Assert.Equal(5, clip.StartBeat, 6);
        timeline.UndoService.Redo();
        Assert.Equal(6.5, clip.StartBeat, 6);
    }

    [AvaloniaFact]
    public void CompleteDrag_DoesNotRecordUnchangedTiming()
    {
        TimelineEditorViewModel timeline = new(
            new IntermediateSongPackage(),
            "root",
            new PlaybackService(),
            new TimelineSettingsService());
        PictogramClipViewModel clip = new(
            new PictogramClip { PictogramId = "picto", Duration = 24 },
            "",
            timeline)
        {
            StartBeat = 5
        };
        SkiaPictogramPreviewControl control = new()
        {
            ActiveTimeline = timeline
        };

        SetField(control, "_draggingClip", clip);
        SetField(control, "_dragOriginalStartBeat", 5d);

        InvokeCompleteDrag(control);

        Assert.False(timeline.UndoService.CanUndo);
    }

    private static void SetField<T>(SkiaPictogramPreviewControl control, string name, T value)
    {
        FieldInfo field = typeof(SkiaPictogramPreviewControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(SkiaPictogramPreviewControl).FullName, name);
        field.SetValue(control, value);
    }

    private static void InvokeCompleteDrag(SkiaPictogramPreviewControl control)
    {
        MethodInfo completeDrag = typeof(SkiaPictogramPreviewControl).GetMethod("CompleteDrag", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(SkiaPictogramPreviewControl).FullName, "CompleteDrag");
        completeDrag.Invoke(control, [null]);
    }
}
