using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.Tests;

public class RecolorMovesBySectionCommandTests
{
    [Fact]
    public void RecolorCommand_AssignsColors_And_RecordsUndo()
    {
        IntermediateSongPackage package = new();
        // setup sections: two sections 0-10 and 10-20
        package.TimelineStructure.Sections.Add(new SectionSegment { StartBeat = 0, SectionType = SongSectionType.Verse });
        package.TimelineStructure.Sections.Add(new SectionSegment { StartBeat = 10, SectionType = SongSectionType.Chorus });
        package.TimelineStructure.EndBeat = 20;

        TimelineEditorViewModel timeline = new(package, "root", new PlaybackService(), new TimelineSettingsService());

        // create two move clips for same id in different sections
        TrackViewModel track = new();
        MoveClipViewModel c1 = new(new MoveClip { MoveId = "run" }, "root", timeline, isFullBody: false, fallbackDurationFrames: 24)
        {
            StartBeat = 1 // in Verse
        };
        MoveClipViewModel c2 = new(new MoveClip { MoveId = "run" }, "root", timeline, isFullBody: false, fallbackDurationFrames: 24)
        {
            StartBeat = 12 // in Chorus
        };

        track.Clips.Add(c1);
        track.Clips.Add(c2);
        timeline.Tracks.Add(track);

        RecolorMovesBySectionCommand cmd = new();
        TestTimelineContextService context = new(timeline);

        // Run
        cmd.Run(context);

        // Should have recorded undo
        Assert.True(timeline.UndoService.CanUndo);

        // Colors assigned in definitions should not be default gray
        MoveDefinitionViewModel def = timeline.GetOrRegisterMove("run", false);
        Assert.NotEqual(Colors.LightGray, def.Color);

        // Undo should restore colors (redo will reapply)
        timeline.UndoService.Undo();
        // color reverted (likely to default gray)
        Assert.Equal(Colors.LightGray, def.Color);
    }
}