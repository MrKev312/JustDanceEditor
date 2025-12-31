using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    private void HandleResizing(Avalonia.Point point, double ppb, double offset)
    {
        double deltaX = point.X - _resizeStartPointerX;
        double deltaBeats = deltaX / ppb;

        double newStart = _resizeOriginalStart;
        double newDuration = _resizeOriginalDuration;

        if (_isResizingLeft)
        {
            newStart = _resizeOriginalStart + deltaBeats;
            newDuration = _resizeOriginalDuration - deltaBeats;
        }
        else
        {
            newStart = _resizeOriginalStart;
            newDuration = System.Math.Max(0.0, _resizeOriginalDuration + deltaBeats);
        }

        double minDuration = 0.125;
        if (newDuration < minDuration)
        {
            if (_isResizingLeft)
            {
                newStart = _resizeOriginalStart + (_resizeOriginalDuration - minDuration);
                newDuration = minDuration;
            }
            else
            {
                newDuration = minDuration;
            }
        }

        // snapping
        var visualParent = this.GetVisualParent();
        TimelineEditorViewModel? vm = null;
        while (visualParent != null)
        {
            if (visualParent is Control c && c.DataContext is TimelineEditorViewModel t)
            {
                vm = t; break;
            }
            visualParent = visualParent.GetVisualParent();
        }

        if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
        {
            if (_isResizingLeft)
            {
                // Preserve the original end while snapping the start — prevents self-alignment conflicts
                double origEnd = _resizeOriginalStart + _resizeOriginalDuration;
                var best = SnappingService.ChooseBestStartPreserveEnd(newStart, origEnd, vm, vm.Tracks.SelectMany(tr => tr.Clips).Where(o => o != _draggingClip));
                if (System.Math.Abs(best - newStart) <= vm.SnapThreshold)
                {
                    newStart = best;
                    newDuration = origEnd - newStart;
                    if (newDuration < minDuration)
                    {
                        newDuration = minDuration;
                        newStart = origEnd - newDuration;
                    }
                }
            }
            else
            {
                double unconstrainedEnd = newStart + newDuration;
                var bestEnd = SnappingService.ChooseBestEnd(unconstrainedEnd, newStart, vm, vm.Tracks.SelectMany(tr => tr.Clips).Where(o => o != _draggingClip));
                if (System.Math.Abs(bestEnd - unconstrainedEnd) <= vm.SnapThreshold)
                {
                    double candidateDuration = bestEnd - _resizeOriginalStart;
                    if (candidateDuration < minDuration) candidateDuration = minDuration;
                    newDuration = candidateDuration;
                    newStart = _resizeOriginalStart;
                }
            }
        }

        _draggingClip.StartBeat = newStart;
        _draggingClip.DurationBeats = newDuration;
        InvalidateMeasure(); InvalidateVisual();
    }

    private void UpdateResizeCursor(Avalonia.Point point, double ppb, double offset)
    {
        bool cursorSet = false;
        if (Clips != null)
        {
            foreach (var c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                bool isResizableType = c.RawClip is PictogramClip || c.RawClip is KaraokeClip;
                if (isResizableType && point.X >= x - ResizeHitThreshold && point.X <= x + ResizeHitThreshold)
                {
                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                    cursorSet = true; break;
                }
                if (isResizableType && point.X >= x + w - ResizeHitThreshold && point.X <= x + w + ResizeHitThreshold)
                {
                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                    cursorSet = true; break;
                }
            }
        }
        if (!cursorSet) Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void HandleDragging(Avalonia.Point point, double ppb)
    {
        double deltaX2 = point.X - _dragStartPointerX;
        double deltaBeats2 = deltaX2 / PixelsPerBeat;

        double newStart2 = _dragOriginalStartBeat + deltaBeats2;
        if (newStart2 < 0) newStart2 = 0;

        var visualParent2 = this.GetVisualParent();
        TimelineEditorViewModel? vm2 = null;
        while (visualParent2 != null)
        {
            if (visualParent2 is Control c && c.DataContext is TimelineEditorViewModel t)
            {
                vm2 = t; break;
            }
            visualParent2 = visualParent2.GetVisualParent();
        }

        if (vm2 != null && (vm2.SnapToGrid || vm2.SnapToCurrentTimeMarker || vm2.SnapToClips))
        {
            var best = SnappingService.ChooseBestStart(newStart2, _draggingClip.DurationBeats, vm2, vm2.Tracks.SelectMany(tr => tr.Clips).Where(o => o != _draggingClip));
            if (System.Math.Abs(best - newStart2) <= vm2.SnapThreshold)
                newStart2 = best;
        }

        _draggingClip.StartBeat = newStart2;
        InvalidateMeasure(); InvalidateVisual();
    }

    private void CompleteResize()
    {
        _isResizingLeft = _isResizingRight = false;
        var finalClip = _draggingClip;
        var origStart = _resizeOriginalStart;
        var origDur = _resizeOriginalDuration;
        if (finalClip != null)
        {
            var visualParent = this.GetVisualParent();
            while (visualParent != null)
            {
                if (visualParent is Control c && c.DataContext is TimelineEditorViewModel vm)
                {
                    double newStart = finalClip.StartBeat;
                    double newDur = finalClip.DurationBeats;
                    vm.PushUndo(() => { finalClip.StartBeat = origStart; finalClip.DurationBeats = origDur; },
                                 () => { finalClip.StartBeat = newStart; finalClip.DurationBeats = newDur; });
                    break;
                }
                visualParent = visualParent.GetVisualParent();
            }
        }

        _draggingClip = null;
    }

    private void CompleteDrag()
    {
        _isDragging = false;
        var finalClip = _draggingClip;
        var original = _dragOriginalStartBeat;
        if (finalClip != null)
        {
            var visualParent = this.GetVisualParent();
            while (visualParent != null)
            {
                if (visualParent is Control c && c.DataContext is TimelineEditorViewModel vm)
                {
                    double newStart = finalClip.StartBeat;
                    vm.PushUndo(() => finalClip.StartBeat = original, () => finalClip.StartBeat = newStart);
                    break;
                }
                visualParent = visualParent.GetVisualParent();
            }
        }

        _draggingClip = null;
    }
}
