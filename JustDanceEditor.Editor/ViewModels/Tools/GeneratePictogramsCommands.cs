using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Move Names", "Timeline/Generate Pictos")]
public sealed class GeneratePictogramsFromMoveNamesCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        _ = timeline.GeneratePictogramsAsync(PictogramGenerationMode.MoveName);
    }
}

[RunCommand("Video Screenshot...", "Timeline/Generate Pictos")]
public sealed class GeneratePictogramsFromVideoCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        _ = RunAsync(timeline);
    }

    private static async Task RunAsync(TimelineEditorViewModel timeline)
    {
        if (timeline.Services.Dialogs == null)
            return;

        PictogramScreenshotOptionsViewModel vm = new();
        PictogramScreenshotOptionsResult? result = await timeline.Services.Dialogs.ShowDialogAsync<PictogramScreenshotOptionsResult>(vm);
        if (result == null)
            return;

        await timeline.GeneratePictogramsAsync(
            PictogramGenerationMode.VideoFrame,
            result.FrameLayoutMode,
            result.HorizontalFocus);
    }
}
