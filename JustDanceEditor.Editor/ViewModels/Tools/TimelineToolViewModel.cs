using CommunityToolkit.Mvvm.ComponentModel;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

/// <summary>
/// A base class for tools that depend on the currently active TimelineEditor.
/// It automatically manages timeline subscriptions and provides virtual methods for timeline lifecycle.
/// This is the "source of truth" for timeline subscription logic across all tool windows.
/// </summary>
public abstract partial class TimelineToolViewModel : Tool
{
    protected readonly ITimelineContextService? TimelineContext;
    private EventHandler? _playbackTimeChangedHandler;
    private PropertyChangedEventHandler? _timelinePropertyChangedHandler;

    [ObservableProperty]
    public partial TimelineEditorViewModel? ActiveTimeline { get; set; }

    protected TimelineToolViewModel()
    {
        // Initial state from global context
        if (Avalonia.Application.Current is App app)
        {
            TimelineContext = app.TimelineContext;
            TimelineContext.PropertyChanged += Context_PropertyChanged;
            ActiveTimeline = TimelineContext.ActiveTimeline;
        }
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.ActiveTimeline))
        {
            ActiveTimeline = TimelineContext?.ActiveTimeline;
        }
    }

    partial void OnActiveTimelineChanging(TimelineEditorViewModel? oldValue, TimelineEditorViewModel? newValue)
    {
        // Detach from previous timeline (do this before the property changes)
        if (oldValue != null)
        {
            UnsubscribeFromTimeline(oldValue);
        }
    }

    partial void OnActiveTimelineChanged(TimelineEditorViewModel? value)
    {
        // Attach to new timeline (property has been updated, ActiveTimeline == value)
        if (value != null)
        {
            SubscribeToTimeline(value);
        }

        OnTimelineAttached(value);
    }

    /// <summary>
    /// Called when a timeline is attached. Override to set up initial state.
    /// </summary>
    protected virtual void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
    }

    /// <summary>
    /// Called when a timeline is detached. Override to clean up state.
    /// </summary>
    protected virtual void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
    }

    /// <summary>
    /// Called when the playback time changes. Override for time-based updates.
    /// </summary>
    protected virtual void OnTimeChanged()
    {
    }

    private void SubscribeToTimeline(TimelineEditorViewModel timeline)
    {
        // Subscribe to playback time changes
        _playbackTimeChangedHandler = (s, e) => OnTimeChanged();
        timeline.Playback.TimeChanged += _playbackTimeChangedHandler;

        // Subscribe to timeline property changes
        _timelinePropertyChangedHandler = (s, e) => OnTimelinePropertyChanged(e.PropertyName);
        timeline.PropertyChanged += _timelinePropertyChangedHandler;
    }

    private void UnsubscribeFromTimeline(TimelineEditorViewModel timeline)
    {
        if (_playbackTimeChangedHandler != null)
        {
            timeline.Playback.TimeChanged -= _playbackTimeChangedHandler;
            _playbackTimeChangedHandler = null;
        }

        if (_timelinePropertyChangedHandler != null)
        {
            timeline.PropertyChanged -= _timelinePropertyChangedHandler;
            _timelinePropertyChangedHandler = null;
        }

        OnTimelineDetached(timeline);
    }

    /// <summary>
    /// Called when any property on the active timeline changes. Override for property-specific logic.
    /// </summary>
    protected virtual void OnTimelinePropertyChanged(string? propertyName)
    {
    }
}