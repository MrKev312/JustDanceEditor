using Avalonia.Controls;
using JustDanceEditor.Editor.ViewModels.Timeline;
using System;
using Avalonia;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineEditorView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private double _lastPixelsPerBeat;

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

        if (_scrollViewer != null)
        {
            _scrollViewer.EffectiveViewportChanged += (s, e) =>
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
        }
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Avalonia.Input.Key.Space && DataContext is TimelineEditorViewModel vm)
        {
            vm.TogglePlayPause();
            e.Handled = true;
        }
    }

    private bool _isInitialFitNeeded = false;

    private DateTime _lastScrollTime = DateTime.MinValue;

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TimelineEditorViewModel vm || _scrollViewer == null) return;

        if (e.PropertyName == nameof(TimelineEditorViewModel.PixelsPerBeat))
        {
            // Zoom from middle
            double oldPpb = _lastPixelsPerBeat;
            double newPpb = vm.PixelsPerBeat;
            _lastPixelsPerBeat = newPpb;

            double viewportWidth = _scrollViewer.Viewport.Width;
            double currentOffset = _scrollViewer.Offset.X;
            
            // Beat at middle of screen
            double centerBeat = (currentOffset + viewportWidth / 2.0) / oldPpb;
            
            // New offset to keep centerBeat at the middle
            double newOffset = centerBeat * newPpb - viewportWidth / 2.0;
            
            _scrollViewer.Offset = new Vector(Math.Max(0, newOffset), _scrollViewer.Offset.Y);
        }
        else if (e.PropertyName == nameof(TimelineEditorViewModel.CurrentBeat))
        {
            if (vm.Playback.IsPlaying)
            {
                // Throttle auto-scroll to ~10 times per second
                if ((DateTime.UtcNow - _lastScrollTime).TotalMilliseconds < 100) return;
                _lastScrollTime = DateTime.UtcNow;

                double ppb = vm.PixelsPerBeat;
                double x = (vm.CurrentBeat - vm.BeatOffset) * ppb;
                double viewportWidth = _scrollViewer.Viewport.Width;
                double currentOffset = _scrollViewer.Offset.X;

                // If playhead is outside the middle 60% of the screen, scroll to it
                double margin = viewportWidth * 0.2;
                if (x < currentOffset + margin || x > currentOffset + viewportWidth - margin)
                {
                    double targetOffset = x - viewportWidth / 2.0;
                    _scrollViewer.Offset = new Vector(Math.Max(0, targetOffset), _scrollViewer.Offset.Y);
                }
            }
        }
    }

    private void TimelineScroll_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (DataContext is not TimelineEditorViewModel vm || _scrollViewer == null) return;

        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            // Zooming
            double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            vm.PixelsPerBeat *= zoomFactor;
            e.Handled = true;
        }
        else
        {
            // Horizontal scrolling
            // Swap vertical wheel to horizontal if no shift is pressed (standard editor behavior)
            double scrollAmount = e.Delta.Y * -50.0; // Adjust sensitivity
            double newOffset = _scrollViewer.Offset.X + scrollAmount;
            _scrollViewer.Offset = new Vector(Math.Max(0, newOffset), _scrollViewer.Offset.Y);
            e.Handled = true;
        }
    }
}