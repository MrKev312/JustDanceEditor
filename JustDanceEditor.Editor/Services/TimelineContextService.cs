using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

public partial class TimelineContextService : ObservableObject, ITimelineContextService
{
    [ObservableProperty]
    public partial TimelineEditorViewModel? ActiveTimeline { get; set; }

    [ObservableProperty]
    public partial List<object> SelectedObjects { get; set; } = [];

    public void UpdateActiveTimeline(TimelineEditorViewModel? timeline)
    {
        if (ActiveTimeline == timeline)
            return;

        ActiveTimeline = timeline;
        // Clear selection when changing timeline context
        SelectedObjects = [];
        WeakReferenceMessenger.Default.Send(new ActiveTimelineChangedMessage(timeline));
    }

    partial void OnSelectedObjectsChanged(List<object> value)
    {
        WeakReferenceMessenger.Default.Send(new SelectionChangedMessage(value));
    }
}

public class ActiveTimelineChangedMessage(TimelineEditorViewModel? value) : ValueChangedMessage<TimelineEditorViewModel?>(value);
public class SelectionChangedMessage(List<object> value) : ValueChangedMessage<List<object>>(value);
