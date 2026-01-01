using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Timeline.Interactions;

/// <summary>
/// Handles resizing of clip edges (left/right) in the timeline.
/// </summary>
public class ClipResizeHandler(TimelineTrackPanel panel)
{
    private readonly TimelineTrackPanel _panel = panel;

    private bool _isResizingLeft;
    private bool _isResizingRight;
    private ClipViewModel? _resizingClip;
    private double _resizeStartPointerX;
    private double _resizeOriginalStart;
    private double _resizeOriginalDuration;

    private const double ResizeHitThreshold = 6.0;

    public bool IsActive => _isResizingLeft || _isResizingRight;

    public static bool IsNearLeft(double localX) => localX <= ResizeHitThreshold;

    public static bool IsNearRight(double localX, double width) => localX >= (width - ResizeHitThreshold);

    public void StartResizeLeft(ClipViewModel clip, Point pointerPos, PointerEventArgs e)
    {
        _resizingClip = clip;
        _isResizingLeft = true;
        _isResizingRight = false;
        _resizeStartPointerX = pointerPos.X;
        _resizeOriginalStart = clip.StartBeat;
        _resizeOriginalDuration = clip.DurationBeats;

        try { e.Pointer.Capture(_panel); } catch { }
    }

    public void StartResizeRight(ClipViewModel clip, Point pointerPos, PointerEventArgs e)
    {
        _resizingClip = clip;
        _isResizingRight = true;
        _isResizingLeft = false;
        _resizeStartPointerX = pointerPos.X;
        _resizeOriginalStart = clip.StartBeat;
        _resizeOriginalDuration = clip.DurationBeats;

        try { e.Pointer.Capture(_panel); } catch { }
    }

    public void UpdateResize(Point pointerPos, double pixelsPerBeat)
    {
        if (_resizingClip == null)
            return;

        double deltaX = pointerPos.X - _resizeStartPointerX;
        double deltaBeats = deltaX / pixelsPerBeat;

        if (_isResizingLeft)
        {
            double newStart = _resizeOriginalStart + deltaBeats;
            double newDuration = _resizeOriginalDuration - deltaBeats;

            if (newDuration >= 0.5)
            {
                _resizingClip.StartBeat = newStart;
                _resizingClip.DurationBeats = newDuration;
            }
        }
        else if (_isResizingRight)
        {
            double newDuration = _resizeOriginalDuration + deltaBeats;

            if (newDuration >= 0.5)
            {
                _resizingClip.DurationBeats = newDuration;
            }
        }

        _panel.InvalidateVisual();
    }

    public void Complete(TimelineEditorViewModel? vm, PointerEventArgs? e = null)
    {
        if (_resizingClip == null)
            return;

        bool changed = Math.Abs(_resizingClip.StartBeat - _resizeOriginalStart) > 0.001 ||
                       Math.Abs(_resizingClip.DurationBeats - _resizeOriginalDuration) > 0.001;

        if (changed && vm != null)
        {
            var finalStart = _resizingClip.StartBeat;
            var finalDuration = _resizingClip.DurationBeats;

            vm.PushUndo(
                undo: () =>
                {
                    _resizingClip.StartBeat = _resizeOriginalStart;
                    _resizingClip.DurationBeats = _resizeOriginalDuration;
                },
                redo: () =>
                {
                    _resizingClip.StartBeat = finalStart;
                    _resizingClip.DurationBeats = finalDuration;
                }
            );
        }

        _isResizingLeft = false;
        _isResizingRight = false;
        _resizingClip = null;

        try { e?.Pointer.Capture(null); } catch { }
    }

    public void Cancel()
    {
        if (_resizingClip == null)
            return;

        _resizingClip.StartBeat = _resizeOriginalStart;
        _resizingClip.DurationBeats = _resizeOriginalDuration;

        _isResizingLeft = false;
        _isResizingRight = false;
        _resizingClip = null;
    }

    public void UpdateCursor(Point pointerPos, double pixelsPerBeat, int beatOffset, IEnumerable<ClipViewModel> clips)
    {
        if (clips == null)
            return;

        StandardCursorType cursorType = StandardCursorType.Arrow;

        foreach (ClipViewModel clip in clips)
        {
            double x = (clip.StartBeat - beatOffset) * pixelsPerBeat;
            double w = clip.DurationBeats * pixelsPerBeat;

            if (pointerPos.X >= x && pointerPos.X <= x + w)
            {
                double localX = pointerPos.X - x;
                bool isResizable = clip.RawClip is PictogramClip or KaraokeClip;

                if (isResizable && IsNearLeft(localX))
                {
                    cursorType = StandardCursorType.SizeWestEast;
                    break;
                }
                else if (isResizable && IsNearRight(localX, w))
                {
                    cursorType = StandardCursorType.SizeWestEast;
                    break;
                }
            }
        }

        _panel.Cursor = new Cursor(cursorType);
    }
}