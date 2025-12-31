using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;
using System.ComponentModel;

namespace JustDanceEditor.Editor.Services;

public interface ITimelineContextService : INotifyPropertyChanged
{
    TimelineEditorViewModel? ActiveTimeline { get; }
    List<object> SelectedObjects { get; set; }
    void UpdateActiveTimeline(TimelineEditorViewModel? timeline);
}
