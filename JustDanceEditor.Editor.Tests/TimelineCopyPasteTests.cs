using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class TimelineCopyPasteTests
{
    [Fact]
    public void CopyPaste_SelectedPictogram_PastesAtPlayhead_AndSupportsUndoRedo()
    {
        IntermediateSongPackage package = CreateBasePackage();
        package.Pictograms.Clips.Add(new PictogramClip
        {
            PictogramId = "picto_a",
            Duration = 24,
            StartTime = 48
        });

        TimelineEditorViewModel timeline = CreateTimeline(package);
        TrackViewModel pictogramTrack = GetTrack(timeline, TrackType.Pictogram);
        PictogramClipViewModel original = Assert.IsType<PictogramClipViewModel>(Assert.Single(pictogramTrack.Clips));
        original.IsSelected = true;

        timeline.CopySelectedClips();
        timeline.CurrentBeat = 12.0;
        timeline.PasteCopiedClips();

        Assert.Equal(2, pictogramTrack.Clips.Count);

        PictogramClipViewModel pasted = pictogramTrack.Clips
            .OfType<PictogramClipViewModel>()
            .Single(c => !ReferenceEquals(c, original));

        Assert.Equal(2.0, original.StartBeat, 6);
        Assert.Equal(12.0, pasted.StartBeat, 6);
        Assert.True(pasted.IsSelected);
        Assert.False(original.IsSelected);

        timeline.UndoService.Undo();
        Assert.Single(pictogramTrack.Clips);
        Assert.True(original.IsSelected);

        timeline.UndoService.Redo();
        Assert.Equal(2, pictogramTrack.Clips.Count);
        Assert.True(pasted.IsSelected);
    }

    [Fact]
    public void CopyPaste_MultiTrackSelection_PreservesRelativeStartAndTrack()
    {
        IntermediateSongPackage package = CreateBasePackage();
        package.Lyrics.Clips.Add(new KaraokeClip
        {
            Lyrics = "hey",
            Duration = 24,
            StartTime = 96
        });
        package.Pictograms.Clips.Add(new PictogramClip
        {
            PictogramId = "picto_b",
            Duration = 24,
            StartTime = 120
        });

        TimelineEditorViewModel timeline = CreateTimeline(package);
        TrackViewModel lyricsTrack = GetTrack(timeline, TrackType.Lyrics);
        TrackViewModel pictogramTrack = GetTrack(timeline, TrackType.Pictogram);

        KaraokeClipViewModel lyrics = Assert.IsType<KaraokeClipViewModel>(Assert.Single(lyricsTrack.Clips));
        PictogramClipViewModel pictogram = Assert.IsType<PictogramClipViewModel>(Assert.Single(pictogramTrack.Clips));

        lyrics.IsSelected = true;
        pictogram.IsSelected = true;

        timeline.CopySelectedClips();
        timeline.CurrentBeat = 8.0;
        timeline.PasteCopiedClips();

        Assert.Equal(2, lyricsTrack.Clips.Count);
        Assert.Equal(2, pictogramTrack.Clips.Count);

        List<double> lyricStarts = [.. lyricsTrack.Clips.Select(c => c.StartBeat).OrderBy(v => v)];
        List<double> pictogramStarts = [.. pictogramTrack.Clips.Select(c => c.StartBeat).OrderBy(v => v)];

        Assert.Equal([4.0, 8.0], lyricStarts);
        Assert.Equal([5.0, 9.0], pictogramStarts);
    }

    [Fact]
    public void CopyPaste_FullBodyMove_PreservesMoveProperties()
    {
        IntermediateSongPackage package = CreateBasePackage();
        package.FullBodyCoachTimelines[0].Clips.Add(new MoveClip
        {
            MoveId = "move_full",
            IsGoldMove = true,
            StartTime = 72
        });

        TimelineEditorViewModel timeline = CreateTimeline(package);
        TrackViewModel fullBodyTrack = GetTrack(timeline, TrackType.CoachFullBody);

        MoveClipViewModel original = Assert.IsType<MoveClipViewModel>(Assert.Single(fullBodyTrack.Clips));
        original.IsSelected = true;

        timeline.CopySelectedClips();
        timeline.CurrentBeat = 1.0;
        timeline.PasteCopiedClips();

        Assert.Equal(2, fullBodyTrack.Clips.Count);

        MoveClipViewModel pasted = fullBodyTrack.Clips
            .OfType<MoveClipViewModel>()
            .Single(c => !ReferenceEquals(c, original));

        Assert.True(pasted.IsFullBody);
        Assert.True(pasted.IsGoldMove);
        Assert.Equal("move_full", pasted.MoveId);
        Assert.Equal(1.0, pasted.StartBeat, 6);
    }

    private static TrackViewModel GetTrack(TimelineEditorViewModel timeline, TrackType trackType)
    {
        return timeline.Tracks.First(t => t.TrackType == trackType);
    }

    private static TimelineEditorViewModel CreateTimeline(IntermediateSongPackage package)
    {
        return new TimelineEditorViewModel(package, "root", new PlaybackService(), new TimelineSettingsService());
    }

    private static IntermediateSongPackage CreateBasePackage()
    {
        IntermediateSongPackage package = new();
        package.TimelineStructure.StartBeat = 0;
        package.TimelineStructure.EndBeat = 256;

        package.CoachTimelines.Add(new MoveTimeline { CoachId = 0, TrackId = 1 });
        package.FullBodyCoachTimelines.Add(new MoveTimeline { CoachId = 0, TrackId = 2 });

        package.HandCoachMoves["move_hand"] = new CoachMoveDefinition
        {
            Color = "#CCCCCC",
            Duration = 24,
            MoveType = CoachMoveType.HandTracking
        };

        package.FullBodyCoachMoves["move_full"] = new CoachMoveDefinition
        {
            Color = "#00CC88",
            Duration = 24,
            MoveType = CoachMoveType.FullBodyTracking
        };

        return package;
    }
}