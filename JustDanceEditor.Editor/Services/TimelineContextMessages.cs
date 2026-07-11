using CommunityToolkit.Mvvm.Messaging.Messages;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

public sealed class ActiveTimelineChangedMessage(TimelineEditorViewModel? value)
    : ValueChangedMessage<TimelineEditorViewModel?>(value);

public sealed class SelectionChangedMessage(List<object> value)
    : ValueChangedMessage<List<object>>(value);
