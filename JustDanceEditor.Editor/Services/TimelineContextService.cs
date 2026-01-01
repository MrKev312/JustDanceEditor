using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;
using System.Linq;

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

        // When switching to a new timeline, try to preserve any currently selected objects
        if (timeline == null)
        {
            SelectedObjects = [];
        }
        else
        {
            // Gather selected clips across all tracks in the timeline
            SelectedObjects = timeline.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>().ToList();
        }

        WeakReferenceMessenger.Default.Send(new ActiveTimelineChangedMessage(timeline));
    }

    public void DetachTimeline(TimelineEditorViewModel timeline)
    {
        if (ActiveTimeline != timeline)
            return;

        UpdateActiveTimeline(null);
    }

    partial void OnSelectedObjectsChanged(List<object> value)
    {
        WeakReferenceMessenger.Default.Send(new SelectionChangedMessage(value));
    }
}

public class ActiveTimelineChangedMessage(TimelineEditorViewModel? value) : ValueChangedMessage<TimelineEditorViewModel?>(value);
public class SelectionChangedMessage(List<object> value) : ValueChangedMessage<List<object>>(value);