using System;
using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Save Map", "File", priority: 90)]
public class SaveMapCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        var timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        try
        {
            timeline.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save map: {ex.Message}");
        }
    }
}