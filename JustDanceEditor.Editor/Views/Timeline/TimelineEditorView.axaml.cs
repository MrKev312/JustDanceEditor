using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Behaviors;

using System;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineEditorView : UserControl
{
    private TimelineNavigationBehavior? _navigationBehavior;

    public TimelineEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var scrollViewer = this.FindControl<ScrollViewer>("TimelineScroll");
        if (scrollViewer != null)
        {
            // Create and attach the behavior
            _navigationBehavior = new TimelineNavigationBehavior();
            TimelineNavigationBehavior.SetInstance(scrollViewer, _navigationBehavior);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is TimelineEditorViewModel vm && Application.Current is App app)
        {
            app.TimelineContext.DetachTimeline(vm);
        }

        // Clean up behavior
        var scrollViewer = this.FindControl<ScrollViewer>("TimelineScroll");
        if (scrollViewer != null && _navigationBehavior != null)
        {
            TimelineNavigationBehavior.SetInstance(scrollViewer, null);
            _navigationBehavior = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space && DataContext is TimelineEditorViewModel vm)
        {
            vm.TogglePlayPause();
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        this.Focus();
    }
}