using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Pictogram Preview", "View/Preview")]
public partial class PictogramPreviewViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    public partial double CurrentBeat { get; set; }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        CurrentBeat = timeline?.CurrentBeat ?? 0;
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        CurrentBeat = 0;
    }

    protected override void OnTimeChanged()
    {
        CurrentBeat = ActiveTimeline?.CurrentBeat ?? 0;
    }
}