using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Tools;

/// <summary>
/// A base class for tools that depend on the currently active TimelineEditor.
/// It automatically manages timeline subscriptions and provides virtual methods for timeline lifecycle.
/// This is the "source of truth" for timeline subscription logic across all tool windows.
/// </summary>
public abstract partial class TimelineToolViewModel : Tool, IDisposable
{
    protected readonly ITimelineContextService? TimelineContext;
    private EventHandler? _playbackTimeChangedHandler;
    private PropertyChangedEventHandler? _timelinePropertyChangedHandler;

    public TimelineEditorViewModel? ActiveTimeline
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            TimelineEditorViewModel? oldValue = field;
            if (oldValue != null)
                UnsubscribeFromTimeline(oldValue);

            field = value;
            OnPropertyChanged(nameof(ActiveTimeline));

            if (value != null)
                SubscribeToTimeline(value);

            OnTimelineAttached(value);
        }
    }

    protected TimelineToolViewModel(
        ITimelineContextService? timelineContext = null,
        bool deferInitialTimelineAttachment = false)
    {
        TimelineContext = timelineContext;
        if (!deferInitialTimelineAttachment)
            InitializeTimelineContext();
    }

    protected void InitializeTimelineContext()
    {
        if (TimelineContext == null)
            return;

        TimelineContext.PropertyChanged -= Context_PropertyChanged;
        TimelineContext.PropertyChanged += Context_PropertyChanged;
        SyncActiveTimelineFromContext();
    }

    public virtual void Dispose()
    {
        if (TimelineContext != null)
            TimelineContext.PropertyChanged -= Context_PropertyChanged;
        ActiveTimeline = null;
        GC.SuppressFinalize(this);
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.ActiveTimeline))
        {
            ActiveTimeline = TimelineContext?.ActiveTimeline;
        }
    }

    private void SyncActiveTimelineFromContext()
    {
        ActiveTimeline = TimelineContext?.ActiveTimeline;
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
