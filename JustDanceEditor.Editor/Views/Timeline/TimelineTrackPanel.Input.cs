using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        if (e.ClickCount == 2 && Clips != null)
        {
            ClipViewModel? targetClip = null;
            double targetStartX = 0, targetWidth = 0;

            foreach (var c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                if (point.X >= x && point.X <= x + w)
                {
                    targetClip = c;
                    targetStartX = x;
                    targetWidth = w;
                    break;
                }
            }

            if (targetClip != null)
            {
                var visualParent = this.GetVisualParent();
                while (visualParent != null)
                {
                    if (visualParent is Control c && c.DataContext is TimelineEditorViewModel vm)
                    {
                        double localX = point.X - targetStartX;
                        bool nearRight = localX >= (targetWidth - ResizeHitThreshold);
                        if (nearRight)
                            vm.Playback.SeekToBeat(targetClip.StartBeat + targetClip.DurationBeats);
                        else
                            vm.Playback.SeekToBeat(targetClip.StartBeat);

                        e.Handled = true;
                        return;
                    }
                    visualParent = visualParent.GetVisualParent();
                }
            }
        }

        if (Clips != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClipViewModel? targetClip = null;
            double targetStartX = 0, targetWidth = 0;

            foreach (var c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                if (point.X >= x && point.X <= x + w)
                {
                    targetClip = c;
                    targetStartX = x;
                    targetWidth = w;
                    break;
                }
            }

            if (targetClip != null)
            {
                double localX = point.X - targetStartX;
                bool nearLeft = localX <= ResizeHitThreshold;
                bool nearRight = localX >= (targetWidth - ResizeHitThreshold);

                bool isResizableType = targetClip.RawClip is PictogramClip || targetClip.RawClip is KaraokeClip;
                _draggingClip = targetClip;

                if (isResizableType && nearLeft)
                {
                    _isResizingLeft = true;
                    _isResizingRight = false;
                    _resizeStartPointerX = point.X;
                    _resizeOriginalStart = targetClip.StartBeat;
                    _resizeOriginalDuration = targetClip.DurationBeats;
                    try { e.Pointer.Capture(this); } catch { }
                    e.Handled = true;
                    return;
                }
                else if (isResizableType && nearRight)
                {
                    _isResizingRight = true;
                    _isResizingLeft = false;
                    _resizeStartPointerX = point.X;
                    _resizeOriginalStart = targetClip.StartBeat;
                    _resizeOriginalDuration = targetClip.DurationBeats;
                    try { e.Pointer.Capture(this); } catch { }
                    e.Handled = true;
                    return;
                }
                else
                {
                    _dragStartPointerX = point.X;
                    _dragOriginalStartBeat = targetClip.StartBeat;
                    _isDragging = true;
                    try { e.Pointer.Capture(this); } catch { }
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetCurrentPoint(this).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        if ((_isResizingLeft || _isResizingRight) && _draggingClip != null)
        {
            HandleResizing(point, ppb, offset);
            return;
        }

        if (!_isDragging || _draggingClip == null)
        {
            UpdateResizeCursor(point, ppb, offset);
            return;
        }

        HandleDragging(point, ppb);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isResizingLeft || _isResizingRight)
        {
            CompleteResize();
            return;
        }

        if (_isDragging)
        {
            CompleteDrag();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _isDragging = false;
        _isResizingLeft = false;
        _isResizingRight = false;
        _draggingClip = null;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }
}
