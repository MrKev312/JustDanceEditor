using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineEditorView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private double _lastPixelsPerBeat;

    // scrubbing state
    private bool _isScrubbing = false;

    public TimelineEditorView()
    {
        InitializeComponent();
        _scrollViewer = this.FindControl<ScrollViewer>("TimelineScroll");

        this.PointerPressed += (s, e) => this.Focus();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is TimelineEditorViewModel vm)
            {
                _lastPixelsPerBeat = vm.PixelsPerBeat;
                vm.PropertyChanged += Vm_PropertyChanged;
                _isInitialFitNeeded = true;
            }
        };

        _scrollViewer?.EffectiveViewportChanged += (s, e) =>
        {
            if (DataContext is TimelineEditorViewModel vm && _scrollViewer != null)
            {
                double viewportWidth = _scrollViewer.Viewport.Width;
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
        };

        // Ensure we clear scrubbing state if the pointer capture is lost for any reason
        this.PointerCaptureLost += (s, e) =>
        {
            if (_isScrubbing)
            {
                _isScrubbing = false;
                HideScrubTooltip();
            }
        };
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is TimelineEditorViewModel vm && Application.Current is App app)
        {
            app.TimelineContext.DetachTimeline(vm);
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

    private bool _isInitialFitNeeded = false;

    private DateTime _lastScrollTime = DateTime.MinValue;

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TimelineEditorViewModel vm || _scrollViewer == null)
            return;

        if (e.PropertyName == nameof(TimelineEditorViewModel.PixelsPerBeat))
        {
            // Zoom from middle
            double oldPpb = _lastPixelsPerBeat;
            double newPpb = vm.PixelsPerBeat;
            _lastPixelsPerBeat = newPpb;

            double viewportWidth = _scrollViewer.Viewport.Width;
            double currentOffset = _scrollViewer.Offset.X;

            // Beat at middle of screen
            double centerBeat = (currentOffset + (viewportWidth / 2.0)) / oldPpb;

            // New offset to keep centerBeat at the middle
            double newOffset = (centerBeat * newPpb) - (viewportWidth / 2.0);

            _scrollViewer.Offset = new Vector(Math.Max(0, newOffset), _scrollViewer.Offset.Y);
        }
        else if (e.PropertyName == nameof(TimelineEditorViewModel.CurrentBeat))
        {
            if (vm.Playback.IsPlaying)
            {
                // Throttle auto-scroll to ~10 times per second
                if ((DateTime.UtcNow - _lastScrollTime).TotalMilliseconds < 100)
                    return;
                _lastScrollTime = DateTime.UtcNow;

                double ppb = vm.PixelsPerBeat;
                double x = (vm.CurrentBeat - vm.BeatOffset) * ppb;
                double viewportWidth = _scrollViewer.Viewport.Width;
                double currentOffset = _scrollViewer.Offset.X;

                // If playhead is outside the middle 60% of the screen, scroll to it
                double margin = viewportWidth * 0.2;
                if (x < currentOffset + margin || x > currentOffset + viewportWidth - margin)
                {
                    double targetOffset = x - (viewportWidth / 2.0);
                    _scrollViewer.Offset = new Vector(Math.Max(0, targetOffset), _scrollViewer.Offset.Y);
                }
            }
        }
    }

    private void TimelineScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not TimelineEditorViewModel vm || _scrollViewer == null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Zooming
            double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            vm.PixelsPerBeat *= zoomFactor;
            e.Handled = true;
        }
        else
        {
            // Horizontal scrolling by default (DAW style)
            double scrollAmount = e.Delta.Y * -50.0;
            double newOffset = _scrollViewer.Offset.X + scrollAmount;
            _scrollViewer.Offset = new Vector(Math.Max(0, newOffset), _scrollViewer.Offset.Y);
            e.Handled = true;
        }
    }

    // --- Playhead scrubbing ---
    private void TimelineContent_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TimelineEditorViewModel vm)
            return;

        // avoid re-entering scrubbing if already scrubbing
        if (_isScrubbing)
            return;

        Grid? contentGrid = this.FindControl<Grid>("TimelineContentGrid");
        Visual relVisual = contentGrid ?? (this as Visual)!;

        // Only respond to left button
        if (!e.GetCurrentPoint(relVisual).Properties.IsLeftButtonPressed)
            return;

        _isScrubbing = true;
        Point pt = e.GetCurrentPoint(relVisual).Position;

        // Capture pointer so we receive move/release outside the element
        try
        {
            e.Pointer.Capture(this as IInputElement);
        }
        catch { }

        UpdateScrubPosition(pt.X, vm);
        e.Handled = true;
    }

    private void TimelineContent_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isScrubbing || DataContext is not TimelineEditorViewModel vm)
            return;
        Grid? contentGrid = this.FindControl<Grid>("TimelineContentGrid");
        Visual relVisual = contentGrid ?? (this as Visual)!;
        Point pt = e.GetCurrentPoint(relVisual).Position;
        UpdateScrubPosition(pt.X, vm);
        e.Handled = true;
    }

    private void TimelineContent_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isScrubbing)
            return;
        _isScrubbing = false;

        // Release pointer capture
        try
        {
            e.Pointer.Capture(null);
        }
        catch { }

        e.Handled = true;
    }

    private void UpdateScrubPosition(double localX, TimelineEditorViewModel vm)
    {
        if (_scrollViewer == null)
            return;

        // Use TimelineContentGrid as authoritative content coordinate
        Grid? contentGrid = this.FindControl<Grid>("TimelineContentGrid");
        double xInContent = localX;
        if (contentGrid != null)
        {
            // localX already measured against contentGrid; keep as-is
            xInContent = localX;
        }

        // Convert local X (relative to content grid) to beat
        double globalX = _scrollViewer.Offset.X + xInContent;
        double beat = (globalX / vm.PixelsPerBeat) + vm.BeatOffset;

        // Apply snapping: if snap-to-beat enabled, snap to nearest beat/size
        if (vm.SnapToGrid)
        {
            beat = Math.Round(beat / vm.SnapGridSize) * vm.SnapGridSize;
        }

        // If snap-to-playhead enabled and within threshold, snap to current beat
        if (vm.SnapToCurrentTimeMarker && Math.Abs(beat - vm.CurrentBeat) <= vm.SnapThreshold)
        {
            beat = vm.CurrentBeat;
        }

        vm.Playback.SeekToBeat(beat);
    }

    private void ShowScrubTooltip(double localX, TimelineEditorViewModel vm) { }
    private void HideScrubTooltip() { }

    // Public API for other controls to show/hide the scrub tooltip at a content X coordinate
    public void ShowScrubTooltipAtContentX(double contentX)
    {
        if (DataContext is not TimelineEditorViewModel vm)
            return;
        TextBlock? tb = this.FindControl<TextBlock>("ScrubTooltip");
        if (tb == null || _scrollViewer == null)
            return;

        double globalX = _scrollViewer.Offset.X + contentX;
        double beat = (globalX / vm.PixelsPerBeat) + vm.BeatOffset;
        string txt = $"Beat: {beat:F2}";

        Dispatcher.UIThread.Post(() =>
        {
            tb.Text = txt;
            tb.IsVisible = true;
            Canvas.SetLeft(tb, Math.Max(0, contentX - 20));
            Canvas.SetTop(tb, 0);
        });
    }

    public void HideScrubTooltipPublic()
    {
        HideScrubTooltip();
    }
}