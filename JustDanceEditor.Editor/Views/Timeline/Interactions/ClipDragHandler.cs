using Avalonia;
using Avalonia.Input;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline.Interactions;

/// <summary>
/// Handles single and multi-clip dragging in the timeline.
/// </summary>
public class ClipDragHandler(TimelineTrackPanel panel) : TimelineInteractionHandler(panel)
{
    private bool _isDragging;
    private bool _isMultiDragging;
    private ClipViewModel? _draggingClip;
    private double _dragStartPointerX;
    private double _dragOriginalStartBeat;
    private Dictionary<ClipViewModel, double>? _multiDragOriginalStarts;

    /// <summary>
    /// Clamps a clip's position to timeline bounds.
    /// </summary>
    private static double ClampStartBeat(ClipViewModel clip, double newStart, TimelineEditorViewModel? vm)
    {
        if (vm?.TimelineStructure == null)
            return newStart;

        double minStart = vm.TimelineStructure.StartBeat;
        double maxEnd = vm.TimelineStructure.EndBeat;

        if (clip is VideoClipViewModel)
        {
            double latestAllowedStart = minStart;
            double earliestAllowedStart = maxEnd - clip.DurationBeats;

            if (earliestAllowedStart > latestAllowedStart)
                return latestAllowedStart;

            return Math.Clamp(newStart, earliestAllowedStart, latestAllowedStart);
        }

        newStart = Math.Max(newStart, minStart);
        double clipEnd = newStart + clip.DurationBeats;
        if (clipEnd > maxEnd)
            newStart = Math.Max(minStart, maxEnd - clip.DurationBeats);

        return newStart;
    }

    public bool IsActive => _isDragging || _isMultiDragging;

    public void StartSingleDrag(ClipViewModel clip, Point pointerPos, PointerEventArgs e)
    {
        _draggingClip = clip;
        _dragStartPointerX = pointerPos.X;
        _dragOriginalStartBeat = clip.StartBeat;
        _isDragging = true;

        Capture(e);
    }

    public void StartMultiDrag(IEnumerable<ClipViewModel> selectedClips, Point pointerPos, PointerEventArgs e)
    {
        _isMultiDragging = true;
        _multiDragOriginalStarts = selectedClips.ToDictionary(c => c, c => c.StartBeat);
        _dragStartPointerX = pointerPos.X;

        Capture(e);
    }

    public void UpdateDrag(Point pointerPos, double pixelsPerBeat, TimelineEditorViewModel? vm)
    {
        if (_isDragging && _draggingClip != null)
        {
            double deltaX = pointerPos.X - _dragStartPointerX;
            double deltaBeats = deltaX / pixelsPerBeat;

            double newStart = _dragOriginalStartBeat + deltaBeats;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                // Exclude the clip being dragged so it doesn't snap to itself
                double duration = _draggingClip.DurationBeats;
                double newEnd = newStart + duration;

                double bestStart = SnappingService.FindSnapBeat(newStart, vm, new[] { _draggingClip });
                double bestEnd = SnappingService.FindSnapBeat(newEnd, vm, new[] { _draggingClip });

                double startAdjust = bestStart - newStart;
                double endAdjust = bestEnd - newEnd;

                const double eps = 1e-9;

                bool startSnapped = Math.Abs(startAdjust) > eps;
                bool endSnapped = Math.Abs(endAdjust) > eps;

                // Apply per rules:
                // 1) If both snapped, apply smaller absolute adjustment
                // 2) If only start snapped, apply startAdjust
                // 3) If only end snapped, apply endAdjust
                if (startSnapped && endSnapped)
                {
                    if (Math.Abs(startAdjust) <= Math.Abs(endAdjust))
                        newStart += startAdjust;
                    else
                        newStart = newEnd + endAdjust - duration;
                }
                else if (startSnapped)
                {
                    newStart += startAdjust;
                }
                else if (endSnapped)
                {
                    newStart = newEnd + endAdjust - duration;
                }
            }

            _draggingClip.StartBeat = ClampStartBeat(_draggingClip, newStart, vm);
            _panel.InvalidateVisual();
            return;
        }

        if (_isMultiDragging && _multiDragOriginalStarts != null)
        {
            double deltaX = pointerPos.X - _dragStartPointerX;
            double deltaBeats = deltaX / pixelsPerBeat;

            List<ClipViewModel> selected = [.. _multiDragOriginalStarts.Keys];
            double originalEarliestStart = selected.Min(c => _multiDragOriginalStarts[c]);
            double originalLatestEnd = selected.Max(c => _multiDragOriginalStarts[c] + c.DurationBeats);

            double unconstrainedEarliest = originalEarliestStart + deltaBeats;
            double unconstrainedLatest = originalLatestEnd + deltaBeats;

            double applyDelta = deltaBeats;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                // Exclude selected clips from snapping targets
                List<ClipViewModel> selectedClips = selected;
                double bestStart = SnappingService.FindSnapBeat(unconstrainedEarliest, vm, selectedClips);
                double bestEnd = SnappingService.FindSnapBeat(unconstrainedLatest, vm, selectedClips);

                double startAdjust = bestStart - unconstrainedEarliest;
                double endAdjust = bestEnd - unconstrainedLatest;

                const double eps = 1e-9;

                bool startSnapped = Math.Abs(startAdjust) > eps;
                bool endSnapped = Math.Abs(endAdjust) > eps;

                // Apply rules (same as single-clip):
                if (startSnapped && endSnapped)
                {
                    if (Math.Abs(startAdjust) <= Math.Abs(endAdjust))
                        applyDelta = deltaBeats + startAdjust;
                    else
                        applyDelta = deltaBeats + endAdjust;
                }
                else if (startSnapped)
                {
                    applyDelta = deltaBeats + startAdjust;
                }
                else if (endSnapped)
                {
                    applyDelta = deltaBeats + endAdjust;
                }
            }

            foreach (KeyValuePair<ClipViewModel, double> kv in _multiDragOriginalStarts.ToList())
            {
                double newStart = kv.Value + applyDelta;
                kv.Key.StartBeat = ClampStartBeat(kv.Key, newStart, vm);
            }

            _panel.InvalidateMeasure();
            _panel.InvalidateVisual();
        }
    }

    public void Complete(TimelineEditorViewModel? vm, PointerEventArgs? e = null)
    {
        if (_isDragging && _draggingClip != null)
        {
            // Capture locals so undo/redo lambdas don't reference cleared fields
            ClipViewModel clip = _draggingClip;
            double orig = _dragOriginalStartBeat;
            double newStart = clip.StartBeat;

            if (Math.Abs(newStart - orig) > 0.001)
            {
                vm?.PushUndo(
                    undo: () => clip.StartBeat = orig,
                    redo: () => clip.StartBeat = newStart
                );
            }

            _isDragging = false;
            _draggingClip = null;
            Release(e);
            return;
        }

        if (_isMultiDragging && _multiDragOriginalStarts != null)
        {
            List<(ClipViewModel Clip, double Orig, double New)> changes = [];
            foreach (KeyValuePair<ClipViewModel, double> kv in _multiDragOriginalStarts)
            {
                changes.Add((kv.Key, kv.Value, kv.Key.StartBeat));
            }

            bool hasChanges = changes.Any(ch => Math.Abs(ch.New - ch.Orig) > 0.001);
            if (hasChanges && vm != null)
            {
                vm.PushUndo(
                    undo: () =>
                    {
                        foreach ((ClipViewModel Clip, double Orig, double New) in changes)
                            Clip.StartBeat = Orig;
                    },
                    redo: () =>
                    {
                        foreach ((ClipViewModel Clip, double Orig, double New) in changes)
                            Clip.StartBeat = New;
                    }
                );
            }

            _isMultiDragging = false;
            _multiDragOriginalStarts = null;
            Release(e);
        }
    }

    public void Cancel()
    {
        if (_isDragging && _draggingClip != null)
        {
            _draggingClip.StartBeat = _dragOriginalStartBeat;
            _isDragging = false;
            _draggingClip = null;
        }

        if (_isMultiDragging && _multiDragOriginalStarts != null)
        {
            foreach (KeyValuePair<ClipViewModel, double> kv in _multiDragOriginalStarts)
            {
                kv.Key.StartBeat = kv.Value;
            }

            _isMultiDragging = false;
            _multiDragOriginalStarts = null;
        }
    }
}