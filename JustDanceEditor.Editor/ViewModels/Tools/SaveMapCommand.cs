using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Save Map", "File", priority: 90)]
public class SaveMapCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        try
        {
            timeline.Save();
        }
        catch
        {
            // Failed to save map — swallow exception or report via UI
        }
    }
}