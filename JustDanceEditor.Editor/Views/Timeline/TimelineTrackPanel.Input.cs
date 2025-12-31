using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using JustDanceEditor.Editor.Services;
using System.Linq;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // Find timeline VM early for selection operations
        var visualParentForVm = this.GetVisualParent();
        TimelineEditorViewModel? contextVm = null;
        while (visualParentForVm != null)
        {
            if (visualParentForVm is Control c && c.DataContext is TimelineEditorViewModel t)
            {
                contextVm = t; break;
            }
            visualParentForVm = visualParentForVm.GetVisualParent();
        }

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

            if (targetClip != null && contextVm != null)
            {
                double localX = point.X - targetStartX;
                bool nearRight = localX >= (targetWidth - ResizeHitThreshold);
                if (nearRight)
                    contextVm.Playback.SeekToBeat(targetClip.StartBeat + targetClip.DurationBeats);
                else
                    contextVm.Playback.SeekToBeat(targetClip.StartBeat);

                e.Handled = true;
                return;
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

            // If clicked empty space, clear selection (unless Ctrl pressed)
            var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
            var shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

            if (targetClip == null)
            {
                if (!ctrl && contextVm != null)
                {
                    foreach (var clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                        clip.IsSelected = false;
                    _lastSelectedClip = null;

                    if (Avalonia.Application.Current is App app)
                        app.TimelineContext.SelectedObjects = [];

                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
                return;
            }

            if (targetClip != null)
            {
                // Multi-select with Ctrl toggles selection
                if (ctrl && !shift)
                {
                    targetClip.IsSelected = !targetClip.IsSelected;
                    _lastSelectedClip = targetClip.IsSelected ? targetClip : _lastSelectedClip;
                    UpdateGlobalSelection(contextVm);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                // Shift+click range selection across all tracks by clip list index
                if (shift && contextVm != null)
                {
                    // Build flat list of all clips in order of tracks
                    var allClips = contextVm.Tracks.SelectMany(tr => tr.Clips).ToList();
                    int a = allClips.IndexOf(_lastSelectedClip ?? targetClip);
                    int b = allClips.IndexOf(targetClip);
                    if (a == -1) a = b;
                    int start = Math.Min(a, b);
                    int end = Math.Max(a, b);
                    for (int i = 0; i < allClips.Count; i++)
                        allClips[i].IsSelected = i >= start && i <= end;
                    _lastSelectedClip = targetClip;
                    UpdateGlobalSelection(contextVm);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                // If multiple clips selected across tracks, start multi-drag when clicking any selected clip
                var selected = (contextVm != null) ? contextVm.Tracks.SelectMany(tr => tr.Clips).Where(c => c.IsSelected).ToList() : Clips.Where(c => c.IsSelected).ToList();
                if (selected.Count > 1 && selected.Contains(targetClip))
                {
                    _isMultiDragging = true;
                    _multiDragOriginalStarts = selected.ToDictionary(c => c, c => c.StartBeat);
                    _dragStartPointerX = point.X;
                    try { e.Pointer.Capture(this); } catch { }
                    e.Handled = true;
                    return;
                }

                // If no modifier keys, make this clip the only selected
                if (!ctrl && !shift && contextVm != null)
                {
                    foreach (var clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                        clip.IsSelected = false;
                    targetClip.IsSelected = true;
                    _lastSelectedClip = targetClip;
                    UpdateGlobalSelection(contextVm);
                }

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

    private void UpdateGlobalSelection(TimelineEditorViewModel? vm)
    {
        if (vm == null || Avalonia.Application.Current is not App app) return;
        app.TimelineContext.SelectedObjects = vm.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>().ToList();
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

        if (_isMultiDragging && _multiDragOriginalStarts != null)
        {
            // Need timeline VM to get other clips for snapping and context
            var visualParentForVm = this.GetVisualParent();
            TimelineEditorViewModel? vm = null;
            while (visualParentForVm != null)
            {
                if (visualParentForVm is Control c && c.DataContext is TimelineEditorViewModel t)
                {
                    vm = t; break;
                }
                visualParentForVm = visualParentForVm.GetVisualParent();
            }

            double deltaX = point.X - _dragStartPointerX;
            double deltaBeats = deltaX / PixelsPerBeat;

            // Compute group's original bounds
            var selected = _multiDragOriginalStarts.Keys.ToList();
            double originalEarliestStart = selected.Min(c => _multiDragOriginalStarts[c]);
            double originalLatestEnd = selected.Max(c => _multiDragOriginalStarts[c] + c.DurationBeats);

            double unconstrainedEarliest = originalEarliestStart + deltaBeats;
            double unconstrainedLatest = originalLatestEnd + deltaBeats;

            double applyDelta = deltaBeats;

            if (vm != null && (vm.SnapToGrid || vm.SnapToCurrentTimeMarker || vm.SnapToClips))
            {
                var otherClips = vm.Tracks.SelectMany(tr => tr.Clips).Where(o => !_multiDragOriginalStarts.ContainsKey(o));

                var bestStart = SnappingService.ChooseBestStartPreserveEnd(unconstrainedEarliest, unconstrainedLatest, vm, otherClips);
                var bestEnd = SnappingService.ChooseBestEnd(unconstrainedLatest, unconstrainedEarliest, vm, otherClips);

                double startAdjust = bestStart - unconstrainedEarliest;
                double endAdjust = bestEnd - unconstrainedLatest;

                // Choose smaller absolute adjustment (prefer start if equal)
                if (System.Math.Abs(startAdjust) <= System.Math.Abs(endAdjust) && System.Math.Abs(startAdjust) <= vm.SnapThreshold)
                {
                    applyDelta = deltaBeats + startAdjust;
                }
                else if (System.Math.Abs(endAdjust) < System.Math.Abs(startAdjust) && System.Math.Abs(endAdjust) <= vm.SnapThreshold)
                {
                    applyDelta = deltaBeats + endAdjust;
                }
            }

            // Apply delta to all selected clips
            foreach (var kv in _multiDragOriginalStarts.ToList())
            {
                kv.Key.StartBeat = kv.Value + applyDelta;
            }

            InvalidateMeasure(); InvalidateVisual();
            e.Handled = true;
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

        if (_isMultiDragging)
        {
            _isMultiDragging = false;
            _multiDragOriginalStarts = null;
            try { e.Pointer.Capture(null); } catch { }
            e.Handled = true;
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