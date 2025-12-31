using CommunityToolkit.Mvvm.ComponentModel;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Tools;

/// <summary>
/// A base class for tools that depend on the currently active TimelineEditor.
/// It automatically subscribes to timeline changes.
/// </summary>
public abstract partial class TimelineToolViewModel : Tool
{
    private readonly ITimelineContextService? _timelineContext;

    [ObservableProperty]
    private TimelineEditorViewModel? _activeTimeline;

    protected TimelineToolViewModel()
    {
        // Initial state from global context
        if (Avalonia.Application.Current is App app)
        {
            _timelineContext = app.TimelineContext;
            _timelineContext.PropertyChanged += Context_PropertyChanged;
            ActiveTimeline = _timelineContext.ActiveTimeline;
        }
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.ActiveTimeline))
        {
            ActiveTimeline = _timelineContext?.ActiveTimeline;
        }
    }

    partial void OnActiveTimelineChanged(TimelineEditorViewModel? value)
    {
        HandleActiveTimelineChanged(value);
    }

    protected virtual void HandleActiveTimelineChanged(TimelineEditorViewModel? value)
    {
    }
}
