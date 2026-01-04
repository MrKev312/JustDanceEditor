using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class RemoveEmptyLyricsCommandTests
{
    [Fact]
    public void RemovesEmptyLyricsAndSetsPrevEol_AndRecordsUndo()
    {
        TimelineEditorViewModel timeline = new(new IntermediateSongPackage(), "root");
        // Create a track with Karaoke clips
        TrackViewModel track = new();
        KaraokeClipViewModel c1 = new(new KaraokeClip { Lyrics = "A" }, 24, Colors.Goldenrod, "A", "root", timeline);
        KaraokeClipViewModel c2 = new(new KaraokeClip { Lyrics = "", IsEndOfLine = true }, 24, Colors.Goldenrod, "", "root", timeline);
        KaraokeClipViewModel c3 = new(new KaraokeClip { Lyrics = "B" }, 24, Colors.Goldenrod, "B", "root", timeline);

        track.Clips.Add(c1);
        track.Clips.Add(c2);
        track.Clips.Add(c3);

        timeline.Tracks.Add(track);

        TestTimelineContextService context = new(timeline);
        RemoveEmptyLyricsCommand cmd = new();

        Assert.True(cmd.CanRun(context));

        cmd.Run(context);

        Assert.DoesNotContain(track.Clips, c => string.IsNullOrWhiteSpace(((KaraokeClipViewModel)c).Lyrics));
        Assert.True(c1.IsEndOfLine); // previous should be set

        // Undo should restore: we can call timeline.UndoService.Undo() once
        Assert.True(timeline.UndoService.CanUndo);
        timeline.UndoService.Undo();
        Assert.Contains(c2, track.Clips);
        Assert.False(c1.IsEndOfLine);
    }
}

// small test context implementation
public class TestTimelineContextService(TimelineEditorViewModel active) : ITimelineContextService
{
    public TimelineEditorViewModel? ActiveTimeline { get; set; } = active;
    public List<object> SelectedObjects { get; set; } = [];

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

    public void UpdateActiveTimeline(TimelineEditorViewModel? timeline)
    {
        ActiveTimeline = timeline;
    }

    public void DetachTimeline(TimelineEditorViewModel timeline)
    {
        if (ActiveTimeline == timeline)
            ActiveTimeline = null;
    }
}