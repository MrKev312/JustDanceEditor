using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.ComponentModel;

namespace JustDanceEditor.Editor.Views.Timeline.Behaviors;

/// <summary>
/// Encapsulates scroll and zoom logic for the timeline editor.
/// Handles:
/// - Ctrl+MouseWheel: Zoom in/out from center
/// - MouseWheel (no modifiers): Horizontal scroll (DAW style)
/// - Auto-scroll: Keeps playhead in view while playing
/// 
/// This is applied via XAML attached behavior pattern:
/// &lt;i:Interaction.Behaviors>
///     &lt;behaviors:TimelineNavigationBehavior/>
/// &lt;/i:Interaction.Behaviors>
/// </summary>
public class TimelineNavigationBehavior : AvaloniaObject
{
    private ScrollViewer? _scrollViewer;
    private double _lastPixelsPerBeat;
    private DateTime _lastScrollTime = DateTime.MinValue;
    private bool _isInitialFitNeeded = false;

    public static readonly AttachedProperty<TimelineNavigationBehavior?> InstanceProperty =
        AvaloniaProperty.RegisterAttached<TimelineNavigationBehavior, ScrollViewer, TimelineNavigationBehavior?>(
            "Instance",
            defaultValue: null);

    public static void SetInstance(ScrollViewer target, TimelineNavigationBehavior? value)
    {
        target.SetValue(InstanceProperty, value);
    }

    public static TimelineNavigationBehavior? GetInstance(ScrollViewer target)
    {
        return target.GetValue(InstanceProperty);
    }

    static TimelineNavigationBehavior()
    {
        InstanceProperty.Changed.AddClassHandler<ScrollViewer>(OnInstanceChanged);
    }

    private static void OnInstanceChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is TimelineNavigationBehavior behavior)
        {
            behavior.Attach(scrollViewer);
        }
        else if (e.OldValue is TimelineNavigationBehavior oldBehavior)
        {
            oldBehavior.Detach();
        }
    }

    private void Attach(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;

        // Subscribe to wheel events
        _scrollViewer.PointerWheelChanged += ScrollViewer_PointerWheelChanged;

        // Subscribe to effective viewport changes for fit-to-window
        _scrollViewer.EffectiveViewportChanged += ScrollViewer_EffectiveViewportChanged;

        if (_scrollViewer.DataContext is TimelineEditorViewModel vm)
        {
            _lastPixelsPerBeat = vm.PixelsPerBeat;
            _isInitialFitNeeded = true;
            vm.PropertyChanged += ScrollViewer_PropertyChanged;
        }
    }

    private void Detach()
    {
        if (_scrollViewer is ScrollViewer scrollViewer)
        {
            scrollViewer.PointerWheelChanged -= ScrollViewer_PointerWheelChanged;
            scrollViewer.EffectiveViewportChanged -= ScrollViewer_EffectiveViewportChanged;

            if (scrollViewer.DataContext is TimelineEditorViewModel vm)
            {
                vm.PropertyChanged -= ScrollViewer_PropertyChanged;
            }
        }

        _scrollViewer = null;
    }

    private void ScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_scrollViewer is not ScrollViewer scrollViewer ||
            scrollViewer.DataContext is not TimelineEditorViewModel vm)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Zooming: multiply pixels per beat
            double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            vm.PixelsPerBeat *= zoomFactor;
            e.Handled = true;
        }
        else
        {
            // Horizontal scrolling (DAW style): scroll by mouse delta
            double scrollAmount = e.Delta.Y * -50.0;
            double newOffset = scrollViewer.Offset.X + scrollAmount;
            scrollViewer.Offset = new Vector(System.Math.Max(0, newOffset), scrollViewer.Offset.Y);
            e.Handled = true;
        }
    }

    private void ScrollViewer_EffectiveViewportChanged(object? sender, EventArgs e)
    {
        if (_scrollViewer is not ScrollViewer scrollViewer ||
            scrollViewer.DataContext is not TimelineEditorViewModel vm)
            return;

        double viewportWidth = scrollViewer.Viewport.Width;
        if (viewportWidth > 0 && vm.MaxBeat > 0)
        {
            double fitPpb = viewportWidth / vm.MaxBeat;
            vm.MinZoomPercentage = fitPpb;

            if (_isInitialFitNeeded)
            {
                vm.ZoomPercentage = fitPpb;
                _isInitialFitNeeded = false;
            }
        }
    }

    private void ScrollViewer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_scrollViewer is not ScrollViewer scrollViewer ||
            scrollViewer.DataContext is not TimelineEditorViewModel vm)
            return;

        if (e.PropertyName == nameof(TimelineEditorViewModel.PixelsPerBeat))
        {
            // Zoom from center of screen
            double oldPpb = _lastPixelsPerBeat;
            double newPpb = vm.PixelsPerBeat;
            _lastPixelsPerBeat = newPpb;

            double viewportWidth = scrollViewer.Viewport.Width;
            double currentOffset = scrollViewer.Offset.X;

            // Beat at the center of the screen
            double centerBeat = (currentOffset + (viewportWidth / 2.0)) / oldPpb;

            // New offset to keep centerBeat at the center
            double newOffset = (centerBeat * newPpb) - (viewportWidth / 2.0);

            scrollViewer.Offset = new Vector(System.Math.Max(0, newOffset), scrollViewer.Offset.Y);
        }
        else if (e.PropertyName == nameof(TimelineEditorViewModel.CurrentBeat))
        {
            // Auto-scroll: keep playhead in view
            if (vm.Playback.IsPlaying)
            {
                // Throttle to ~10 times per second
                if ((DateTime.UtcNow - _lastScrollTime).TotalMilliseconds < 100)
                    return;
                _lastScrollTime = DateTime.UtcNow;

                double ppb = vm.PixelsPerBeat;
                double x = (vm.CurrentBeat - vm.BeatOffset) * ppb;
                double viewportWidth = scrollViewer.Viewport.Width;
                double currentOffset = scrollViewer.Offset.X;

                // If playhead is outside the middle 60% of the screen, scroll to it
                double margin = viewportWidth * 0.2;
                if (x < currentOffset + margin || x > currentOffset + viewportWidth - margin)
                {
                    double targetOffset = x - (viewportWidth / 2.0);
                    scrollViewer.Offset = new Vector(System.Math.Max(0, targetOffset), scrollViewer.Offset.Y);
                }
            }
        }
    }
}