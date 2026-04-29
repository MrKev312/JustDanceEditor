using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class TimelinePictogramInsertionTests
{
    [Fact]
    public void BuildPictogramInsertionBeats_NoneMode_ReturnsPlayheadOnly()
    {
        List<MoveClipViewModel> moves =
        [
            new(new MoveClip { MoveId = "moveA", StartTime = 48 }, null, null, isFullBody: false, fallbackDurationFrames: 24)
        ];

        PictogramScreenshotOptionsResult options = new()
        {
            InsertionMode = PictogramInsertionMode.None
        };

        List<double> beats = TimelineEditorViewModel.BuildPictogramInsertionBeats(moves, playheadBeat: 10.5, options);

        double beat = Assert.Single(beats);
        Assert.Equal(10.5, beat, 6);
    }

    [Fact]
    public void BuildPictogramInsertionBeats_AllInstances_AppliesReferenceOffset()
    {
        List<MoveClipViewModel> moves =
        [
            new(new MoveClip { MoveId = "moveB", StartTime = 240 }, null, null, isFullBody: false, fallbackDurationFrames: 24),
            new(new MoveClip { MoveId = "moveB", StartTime = 288 }, null, null, isFullBody: false, fallbackDurationFrames: 24),
            new(new MoveClip { MoveId = "moveB", StartTime = 240 }, null, null, isFullBody: true, fallbackDurationFrames: 24),
            new(new MoveClip { MoveId = "moveA", StartTime = 300 }, null, null, isFullBody: false, fallbackDurationFrames: 24)
        ];

        // Selected reference starts at beat 8.0 (192/24), playhead at beat 9.0 => offset = +1.0
        PictogramScreenshotOptionsResult options = new()
        {
            InsertionMode = PictogramInsertionMode.AllInstancesOfSelectedMove,
            ReferenceMoveId = "moveB",
            ReferenceMoveStartFrame = 192
        };

        List<double> beats = TimelineEditorViewModel.BuildPictogramInsertionBeats(moves, playheadBeat: 9.0, options);

        Assert.Equal(2, beats.Count);
        Assert.Equal(11.0, beats[0], 6); // 10 + 1
        Assert.Equal(13.0, beats[1], 6); // 12 + 1
    }

    [Fact]
    public void BuildPictogramInsertionBeats_AllInstances_FallsBackToPlayheadWhenNoMatches()
    {
        List<MoveClipViewModel> moves =
        [
            new(new MoveClip { MoveId = "moveA", StartTime = 48 }, null, null, isFullBody: false, fallbackDurationFrames: 24)
        ];

        PictogramScreenshotOptionsResult options = new()
        {
            InsertionMode = PictogramInsertionMode.AllInstancesOfSelectedMove,
            ReferenceMoveId = "moveB",
            ReferenceMoveStartFrame = 96
        };

        List<double> beats = TimelineEditorViewModel.BuildPictogramInsertionBeats(moves, playheadBeat: 7.25, options);

        double beat = Assert.Single(beats);
        Assert.Equal(7.25, beat, 6);
    }

    [Fact]
    public void BuildNewPictogramBaseId_UsesReferenceMoveName_WhenAttachedToMove()
    {
        PictogramScreenshotOptionsResult options = new()
        {
            InsertionMode = PictogramInsertionMode.AllInstancesOfSelectedMove,
            ReferenceMoveId = "Move B"
        };

        string id = TimelineEditorViewModel.BuildNewPictogramBaseId(options);

        Assert.Equal("auto_move_b", id);
    }

    [Fact]
    public void BuildNewPictogramBaseId_UsesScreenshot_WhenNoMoveAttached()
    {
        PictogramScreenshotOptionsResult options = new()
        {
            InsertionMode = PictogramInsertionMode.None,
            ReferenceMoveId = null
        };

        string id = TimelineEditorViewModel.BuildNewPictogramBaseId(options);

        Assert.Equal("auto_screenshot", id);
    }
}
