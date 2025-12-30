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
        
        DataContextChanged += (s, e) =>
        {
            if (DataContext is TimelineEditorViewModel vm)
            {
                _lastPixelsPerBeat = vm.PixelsPerBeat;
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        };
    }

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
                // Auto scroll
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
}