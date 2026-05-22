using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class TimelineRenameAndSelectionTests
{
    [Fact]
    public void RenamePictogramId_RenamesFile_AndRelinksAllInstances()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateBasePackage();
            package.Pictograms.Clips.Add(new PictogramClip { PictogramId = "old_picto", StartTime = 24, Duration = 24 });
            package.Pictograms.Clips.Add(new PictogramClip { PictogramId = "old_picto", StartTime = 48, Duration = 24 });

            string oldPath = Path.Combine(root, "assets", "pictograms", "old_picto.webp");
            File.WriteAllBytes(oldPath, [1, 2, 3]);

            TimelineEditorViewModel timeline = new(package, root, new PlaybackService(), new TimelineSettingsService());

            bool ok = timeline.RenamePictogramId("old_picto", "new_picto");

            Assert.True(ok);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(Path.Combine(root, "assets", "pictograms", "new_picto.webp")));
            Assert.All(timeline.Tracks.SelectMany(t => t.Clips).OfType<PictogramClipViewModel>(), c => Assert.Equal("new_picto", c.PictogramId));

            timeline.UndoService.Undo();
            Assert.True(File.Exists(oldPath));
            Assert.False(File.Exists(Path.Combine(root, "assets", "pictograms", "new_picto.webp")));
            Assert.All(timeline.Tracks.SelectMany(t => t.Clips).OfType<PictogramClipViewModel>(), c => Assert.Equal("old_picto", c.PictogramId));

            timeline.UndoService.Redo();
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(Path.Combine(root, "assets", "pictograms", "new_picto.webp")));
            Assert.All(timeline.Tracks.SelectMany(t => t.Clips).OfType<PictogramClipViewModel>(), c => Assert.Equal("new_picto", c.PictogramId));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RenameMoveId_UpdatesCatalog_AndRelinksMatchingMoveClips()
    {
        IntermediateSongPackage package = CreateBasePackage();
        package.CoachTimelines[0].Clips.Add(new MoveClip { MoveId = "old_move", StartTime = 24, IsGoldMove = false });
        package.CoachTimelines[0].Clips.Add(new MoveClip { MoveId = "other", StartTime = 48, IsGoldMove = false });
        package.HandCoachMoves["old_move"] = new CoachMoveDefinition { Color = "#112233", Duration = 24, MoveType = CoachMoveType.HandTracking };
        package.HandCoachMoves["other"] = new CoachMoveDefinition { Color = "#334455", Duration = 24, MoveType = CoachMoveType.HandTracking };

        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        bool ok = timeline.RenameMoveId("old_move", "new_move", isFullBody: false);

        Assert.True(ok);
        Assert.False(package.HandCoachMoves.ContainsKey("old_move"));
        Assert.True(package.HandCoachMoves.ContainsKey("new_move"));

        List<MoveClipViewModel> clips = [.. timeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>()];
        Assert.Contains(clips, c => c.MoveId == "new_move");
        Assert.Contains(clips, c => c.MoveId == "other");

        timeline.UndoService.Undo();
        Assert.True(package.HandCoachMoves.ContainsKey("old_move"));
        Assert.False(package.HandCoachMoves.ContainsKey("new_move"));
        Assert.Contains(clips, c => c.MoveId == "old_move");

        timeline.UndoService.Redo();
        Assert.False(package.HandCoachMoves.ContainsKey("old_move"));
        Assert.True(package.HandCoachMoves.ContainsKey("new_move"));
        Assert.Contains(clips, c => c.MoveId == "new_move");
    }

    [Fact]
    public void SelectAllPictogramInstances_SelectsOnlyMatchingPictograms()
    {
        IntermediateSongPackage package = CreateBasePackage();
        package.Pictograms.Clips.Add(new PictogramClip { PictogramId = "a", StartTime = 24, Duration = 24 });
        package.Pictograms.Clips.Add(new PictogramClip { PictogramId = "a", StartTime = 48, Duration = 24 });
        package.Pictograms.Clips.Add(new PictogramClip { PictogramId = "b", StartTime = 72, Duration = 24 });

        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        IReadOnlyList<PictogramClipViewModel> selected = timeline.SelectAllPictogramInstances("a");

        Assert.Equal(2, selected.Count);
        Assert.All(selected, c => Assert.True(c.IsSelected));
        Assert.All(timeline.Tracks.SelectMany(t => t.Clips).OfType<PictogramClipViewModel>().Where(c => c.PictogramId == "b"), c => Assert.False(c.IsSelected));
    }

    [Fact]
    public void SelectAllMoveInstances_SelectsOnlyMatchingMoves()
    {
        IntermediateSongPackage package = CreateBasePackage();
        package.CoachTimelines[0].Clips.Add(new MoveClip { MoveId = "m1", StartTime = 24, IsGoldMove = false });
        package.CoachTimelines[0].Clips.Add(new MoveClip { MoveId = "m1", StartTime = 48, IsGoldMove = false });
        package.CoachTimelines[0].Clips.Add(new MoveClip { MoveId = "m2", StartTime = 72, IsGoldMove = false });
        package.HandCoachMoves["m1"] = new CoachMoveDefinition { Color = "#112233", Duration = 24, MoveType = CoachMoveType.HandTracking };
        package.HandCoachMoves["m2"] = new CoachMoveDefinition { Color = "#334455", Duration = 24, MoveType = CoachMoveType.HandTracking };

        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        IReadOnlyList<MoveClipViewModel> selected = timeline.SelectAllMoveInstances("m1");

        Assert.Equal(2, selected.Count);
        Assert.All(selected, c => Assert.True(c.IsSelected));
        Assert.All(timeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().Where(c => c.MoveId == "m2"), c => Assert.False(c.IsSelected));
    }

    private static IntermediateSongPackage CreateBasePackage()
    {
        IntermediateSongPackage package = new();
        package.TimelineStructure.StartBeat = 0;
        package.TimelineStructure.EndBeat = 256;
        package.CoachTimelines.Add(new MoveTimeline { CoachId = 0, TrackId = 1 });
        return package;
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "jdi_rename_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "pictograms"));
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}