using JustDanceEditor.Editor.ViewModels.Timeline;

using System.ComponentModel;

namespace JustDanceEditor.Editor.Services;

public interface ITimelineContextService : INotifyPropertyChanged
{
    TimelineEditorViewModel? ActiveTimeline { get; }
    void UpdateActiveTimeline(TimelineEditorViewModel? timeline);
}
