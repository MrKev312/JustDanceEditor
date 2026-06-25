using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class MoveClipViewModelTests
{
    [Fact]
    public void ChangingMoveId_AdoptsDefinitionColorAndDuration()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        // register a move definition and set properties
        MoveDefinitionViewModel def = timeline.GetOrRegisterMove("moveX", false);
        def.Color = Colors.Magenta;
        def.DefaultDuration = 48.0; // frames

        MoveClip raw = new() { MoveId = "old" };
        MoveClipViewModel clipVm = new(raw, rootPath: null, parentTimeline: timeline, isFullBody: false, fallbackDurationFrames: 24);

        // preconditions
        Assert.NotEqual(def, clipVm.Definition);

        // change to new move id
        clipVm.MoveId = "moveX";

        Assert.Same(def, clipVm.Definition);
        // Background color should adopt definition color (normalized opaque)
        Assert.Equal(new Color(255, def.Color.R, def.Color.G, def.Color.B), clipVm.BackgroundColor);
        // DurationBeats should adopt def.DefaultDuration / 24.0
        Assert.Equal(def.DefaultDuration / 24.0, clipVm.DurationBeats, 6);
    }

    [Fact]
    public void BackgroundColorChange_PropagatesToDefinition_WhenNotSuppressing()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        MoveDefinitionViewModel def = timeline.GetOrRegisterMove("m1", false);
        def.Color = Colors.Green;

        MoveClip raw = new() { MoveId = "m1" };
        _ = new MoveClipViewModel(raw, rootPath: null, parentTimeline: timeline, isFullBody: false, fallbackDurationFrames: 24)
        {
            // change BackgroundColor via property on clip
            BackgroundColor = Colors.Blue
        };
        // Definition should be updated to normalized Bg color
        Assert.Equal(new Color(255, Colors.Blue.R, Colors.Blue.G, Colors.Blue.B), def.Color);
    }
}