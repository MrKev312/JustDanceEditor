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

        ScrollViewer? scrollViewer = this.FindControl<ScrollViewer>("TimelineScroll");
        if (scrollViewer != null)
        {
            // Create and attach the behavior
            _navigationBehavior = new TimelineNavigationBehavior();
            TimelineNavigationBehavior.SetInstance(scrollViewer, _navigationBehavior);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is TimelineEditorViewModel vm)
            vm.Services.TimelineContext?.DetachTimeline(vm);

        // Clean up behavior
        ScrollViewer? scrollViewer = this.FindControl<ScrollViewer>("TimelineScroll");
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
        Focus();
    }

    private void ZoomSpinner_OnSpin(object? sender, SpinEventArgs e)
    {
        if (DataContext is not TimelineEditorViewModel vm)
            return;

        vm.ZoomPercentage = e.Direction == SpinDirection.Increase
            ? Math.Min(vm.MaxZoomPercentage, vm.ZoomPercentage + 1)
            : Math.Max(vm.MinZoomPercentage, vm.ZoomPercentage - 1);
    }
}