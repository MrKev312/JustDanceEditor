using Avalonia;
using Avalonia.Input;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;
namespace JustDanceEditor.Editor.Views.Timeline.Interactions;

/// <summary>
/// Handles resizing of clip edges (left/right) in the timeline.
/// </summary>
public class ClipResizeHandler(TimelineTrackPanel panel) : TimelineInteractionHandler(panel)
{
    private bool _isResizingLeft;
    private bool _isResizingRight;
    private ClipViewModel? _resizingClip;
    private double _resizeStartPointerX;
    private double _resizeOriginalStart;
    private double _resizeOriginalDuration;

    private const double ResizeHitThreshold = 6.0;

    /// <summary>
    /// Clamps a clip's position and size to timeline bounds.
    /// </summary>
    private static (double clampedStart, double clampedDuration) ClampToTimelineBounds(ClipViewModel clip, double newStart, double newDuration, TimelineEditorViewModel? vm)
    {
        if (vm?.TimelineStructure == null)
            return (newStart, newDuration);

        double minStart = vm.TimelineStructure.StartBeat;
        double maxEnd = vm.TimelineStructure.EndBeat;
        double availableSpan = maxEnd - minStart;

        // Clamp start to minimum
        newStart = Math.Max(newStart, minStart);
        // Clamp duration so end doesn't exceed max
        double maxEnd_atStart = maxEnd - newStart;
        newDuration = Math.Min(newDuration, maxEnd_atStart);

        return (newStart, newDuration);
    }

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

        Capture(e);
    }

    public void StartResizeRight(ClipViewModel clip, Point pointerPos, PointerEventArgs e)
    {
        _resizingClip = clip;
        _isResizingRight = true;
        _isResizingLeft = false;
        _resizeStartPointerX = pointerPos.X;
        _resizeOriginalStart = clip.StartBeat;
        _resizeOriginalDuration = clip.DurationBeats;

        Capture(e);
    }

    public void UpdateResize(Point pointerPos, double pixelsPerBeat, TimelineEditorViewModel? vm)
    {
        if (_resizingClip == null)
            return;

        double deltaX = pointerPos.X - _resizeStartPointerX;
        double deltaBeats = deltaX / pixelsPerBeat;

        // Prepare all clips (we'll exclude the resizing clip via the excludedClips parameter)
        IEnumerable<ClipViewModel> allClips = vm != null ? vm.Tracks.SelectMany(t => t.Clips) : [];

        if (_isResizingLeft)
        {
            double unconstrainedStart = _resizeOriginalStart + deltaBeats;
            double fixedEnd = _resizeOriginalStart + _resizeOriginalDuration;
            double finalStart = unconstrainedStart;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                finalStart = SnappingService.FindSnapBeat(unconstrainedStart, vm, new[] { _resizingClip });
            }

            double newDuration = fixedEnd - finalStart;

            if (newDuration >= 0.5)
            {
                // Clamp start to timeline bounds, but preserve the fixed end position
                if (vm?.TimelineStructure != null)
                {
                    finalStart = Math.Max(finalStart, vm.TimelineStructure.StartBeat);
                    // Recalculate duration based on fixed end, but don't let end exceed max
                    double clampedEnd = Math.Min(fixedEnd, vm.TimelineStructure.EndBeat);
                    newDuration = clampedEnd - finalStart;
                }

                if (newDuration >= 0.5)
                {
                    _resizingClip.StartBeat = finalStart;
                    _resizingClip.DurationBeats = newDuration;
                }
            }
        }
        else if (_isResizingRight)
        {
            double unconstrainedEnd = _resizeOriginalStart + _resizeOriginalDuration + deltaBeats;
            double finalEnd = unconstrainedEnd;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                finalEnd = SnappingService.FindSnapBeat(unconstrainedEnd, vm, new[] { _resizingClip });
            }

            double newDuration = finalEnd - _resizeOriginalStart;

            if (newDuration >= 0.5)
            {
                (_, newDuration) = ClampToTimelineBounds(_resizingClip, _resizeOriginalStart, newDuration, vm);
                _resizingClip.DurationBeats = newDuration;
            }
        }

        _panel?.InvalidateVisual();
    }

    public void Complete(TimelineEditorViewModel? vm, PointerEventArgs? e = null)
    {
        if (_resizingClip == null)
            return;

        bool changed = Math.Abs(_resizingClip.StartBeat - _resizeOriginalStart) > 0.001 ||
                       Math.Abs(_resizingClip.DurationBeats - _resizeOriginalDuration) > 0.001;

        if (changed && vm != null)
        {
            // Capture the clip locally so the undo/redo lambdas don't reference the cleared field
            ClipViewModel clip = _resizingClip;
            double finalStart = clip.StartBeat;
            double finalDuration = clip.DurationBeats;

            vm.PushUndo(
                undo: () =>
                {
                    clip.StartBeat = _resizeOriginalStart;
                    clip.DurationBeats = _resizeOriginalDuration;
                },
                redo: () =>
                {
                    clip.StartBeat = finalStart;
                    clip.DurationBeats = finalDuration;
                }
            );
        }

        _isResizingLeft = false;
        _isResizingRight = false;
        _resizingClip = null;

        Release(e);
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
                bool isResizable = clip.IsResizable;

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