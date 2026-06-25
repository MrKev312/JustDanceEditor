using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Save Map", "File", priority: 90)]
public class SaveMapCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        _ = RunAsync(timelineContext, GetMainWindow());
    }

    internal static async Task<bool> RunAsync(ITimelineContextService? timelineContext, Window? owner = null)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return false;

        return await timeline.TrySaveAndReportFailureAsync(owner);
    }

    private static Window? GetMainWindow()
        => Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}