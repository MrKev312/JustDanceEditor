using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;

namespace JustDanceEditor.Editor.Services;

public partial class TimelineContextService : ObservableObject, ITimelineContextService
{
    [ObservableProperty]
    private TimelineEditorViewModel? _activeTimeline;

    public void UpdateActiveTimeline(TimelineEditorViewModel? timeline)
    {
        if (ActiveTimeline == timeline) return;

        ActiveTimeline = timeline;
        WeakReferenceMessenger.Default.Send(new ActiveTimelineChangedMessage(timeline));
    }
}

public class ActiveTimelineChangedMessage(TimelineEditorViewModel? value) : ValueChangedMessage<TimelineEditorViewModel?>(value);
