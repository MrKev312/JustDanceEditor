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
    protected readonly ITimelineContextService? TimelineContext;

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

    partial void OnActiveTimelineChanged(TimelineEditorViewModel? value)
    {
        HandleActiveTimelineChanged(value);
    }

    protected virtual void HandleActiveTimelineChanged(TimelineEditorViewModel? value)
    {
    }
}
