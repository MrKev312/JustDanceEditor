using Avalonia;
using Avalonia.Controls;
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

    public void UpdateDrag(Point pointerPos, double pixelsPerBeat, int beatOffset, TimelineEditorViewModel? vm)
    {
        if (_isDragging && _draggingClip != null)
        {
            double deltaX = pointerPos.X - _dragStartPointerX;
            double deltaBeats = deltaX / pixelsPerBeat;

            double newStart = _dragOriginalStartBeat + deltaBeats;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                IEnumerable<ClipViewModel> otherClips = vm.Tracks.SelectMany(tr => tr.Clips)
                    .Where(c => c != _draggingClip);
                newStart = SnappingService.ChooseBestStart(newStart, deltaBeats, vm, otherClips);
            }

            _draggingClip.StartBeat = newStart;
            _panel.InvalidateVisual();
            return;
        }

        if (_isMultiDragging && _multiDragOriginalStarts != null)
        {
            double deltaX = pointerPos.X - _dragStartPointerX;
            double deltaBeats = deltaX / pixelsPerBeat;

            var selected = _multiDragOriginalStarts.Keys.ToList();
            double originalEarliestStart = selected.Min(c => _multiDragOriginalStarts[c]);
            double originalLatestEnd = selected.Max(c => _multiDragOriginalStarts[c] + c.DurationBeats);

            double unconstrainedEarliest = originalEarliestStart + deltaBeats;
            double unconstrainedLatest = originalLatestEnd + deltaBeats;

            double applyDelta = deltaBeats;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                IEnumerable<ClipViewModel> otherClips = vm.Tracks.SelectMany(tr => tr.Clips)
                    .Where(o => !_multiDragOriginalStarts.ContainsKey(o));

                var bestStart = SnappingService.ChooseBestStartPreserveEnd(unconstrainedEarliest, unconstrainedLatest, vm, otherClips);
                var bestEnd = SnappingService.ChooseBestEnd(unconstrainedLatest, unconstrainedEarliest, vm, otherClips);

                double startAdjust = bestStart - unconstrainedEarliest;
                double endAdjust = bestEnd - unconstrainedLatest;

                if (Math.Abs(startAdjust) <= Math.Abs(endAdjust) && Math.Abs(startAdjust) <= vm.SnapThreshold)
                {
                    applyDelta = deltaBeats + startAdjust;
                }
                else if (Math.Abs(endAdjust) < Math.Abs(startAdjust) && Math.Abs(endAdjust) <= vm.SnapThreshold)
                {
                    applyDelta = deltaBeats + endAdjust;
                }
            }

            foreach (KeyValuePair<ClipViewModel, double> kv in _multiDragOriginalStarts.ToList())
            {
                kv.Key.StartBeat = kv.Value + applyDelta;
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
            var clip = _draggingClip;
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
            var changes = new List<(ClipViewModel Clip, double Orig, double New)>();
            foreach (var kv in _multiDragOriginalStarts)
            {
                changes.Add((kv.Key, kv.Value, kv.Key.StartBeat));
            }

            bool hasChanges = changes.Any(ch => Math.Abs(ch.New - ch.Orig) > 0.001);
            if (hasChanges && vm != null)
            {
                vm.PushUndo(
                    undo: () =>
                    {
                        foreach (var ch in changes)
                            ch.Clip.StartBeat = ch.Orig;
                    },
                    redo: () =>
                    {
                        foreach (var ch in changes)
                            ch.Clip.StartBeat = ch.New;
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
            foreach (var kv in _multiDragOriginalStarts)
            {
                kv.Key.StartBeat = kv.Value;
            }

            _isMultiDragging = false;
            _multiDragOriginalStarts = null;
        }
    }
}