using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Editor.Tests;

public sealed class RecordingsToolViewModelTests
{
    [Fact]
    public async Task ActiveTimeline_SetAfterConstruction_AttachesWithoutCrash()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        await using RecordingsToolViewModel tool = new();
        try
        {
            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;

            Assert.Same(timeline, tool.ActiveTimeline);
            Assert.Contains(0, tool.CoachIds);
        }
        finally
        {
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}