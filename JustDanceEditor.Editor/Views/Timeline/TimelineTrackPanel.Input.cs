using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using JustDanceEditor.Editor.Services;
using System.Linq;
using System.Collections.Generic;
using Avalonia;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Ensure we have a background so empty-space hits are delivered
        if (Background == null)
        {
            Background = Brushes.Transparent;
        }

        Point point = e.GetCurrentPoint(this).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // Find timeline VM early for selection operations
        Visual? visualParentForVm = this.GetVisualParent();
        TimelineEditorViewModel? contextVm = null;
        while (visualParentForVm != null)
        {
            if (visualParentForVm is Control c && c.DataContext is TimelineEditorViewModel t)
            {
                contextVm = t;
                break;
            }

            visualParentForVm = visualParentForVm.GetVisualParent();
        }

        if (e.ClickCount == 2 && Clips != null)
        {
            ClipViewModel? targetClip = null;
            double targetStartX = 0, targetWidth = 0;

            foreach (ClipViewModel c in Clips)
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

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClipViewModel? targetClip = null;
            double targetStartX = 0, targetWidth = 0;

            if (Clips != null)
            {
                foreach (ClipViewModel c in Clips)
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
            }

            // If clicked empty space, clear selection (unless Ctrl pressed)
            var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
            var shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

            if (targetClip == null)
            {
                // Start box selection when clicking empty space (left button only)
                _isBoxSelecting = true;
                _boxStartPoint = point;
                _boxCurrentPoint = point;
                try { e.Pointer.Capture(this); } catch { }
                try { this.Focus(); } catch { }
                Cursor = new Cursor(StandardCursorType.Cross);
                InvalidateVisual();
                e.Handled = true;

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
                    // Build flat list of all clips in order of tracks, but sorted by time within tracks
                    var allClips = contextVm.Tracks
                        .SelectMany(tr => tr.Clips.OrderBy(c => c.StartBeat))
                        .ToList();

                    int a = allClips.IndexOf(_lastSelectedClip ?? targetClip);
                    int b = allClips.IndexOf(targetClip);
                    
                    if (a != -1 && b != -1)
                    {
                        int start = Math.Min(a, b);
                        int end = Math.Max(a, b);
                        for (int i = 0; i < allClips.Count; i++)
                            allClips[i].IsSelected = i >= start && i <= end;
                    }
                    
                    _lastSelectedClip = targetClip;
                    UpdateGlobalSelection(contextVm);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                // If multiple clips selected across tracks, start multi-drag when clicking any selected clip
                List<ClipViewModel> selected = (contextVm != null) ? contextVm.Tracks.SelectMany(tr => tr.Clips).Where(c => c.IsSelected).ToList() : Clips.Where(c => c.IsSelected).ToList();
                if (selected.Count > 1 && selected.Contains(targetClip))
                {
                    _isMultiDragging = true;
                    _multiDragOriginalStarts = selected.ToDictionary(c => c, c => c.StartBeat);
                    _dragStartPointerX = point.X;
                    try
                    {
                        e.Pointer.Capture(this);
                    }
                    catch { }

                    e.Handled = true;
                    return;
                }

                // If no modifier keys, make this clip the only selected
                if (!ctrl && !shift && contextVm != null)
                {
                    foreach (ClipViewModel? clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                        clip.IsSelected = false;
                    targetClip.IsSelected = true;
                    _lastSelectedClip = targetClip;
                    UpdateGlobalSelection(contextVm);
                }

                double localX = point.X - targetStartX;
                bool nearLeft = localX <= ResizeHitThreshold;
                bool nearRight = localX >= (targetWidth - ResizeHitThreshold);

                bool isResizableType = targetClip.RawClip is PictogramClip or KaraokeClip;
                _draggingClip = targetClip;

                if (isResizableType && nearLeft)
                {
                    _isResizingLeft = true;
                    _isResizingRight = false;
                    _resizeStartPointerX = point.X;
                    _resizeOriginalStart = targetClip.StartBeat;
                    _resizeOriginalDuration = targetClip.DurationBeats;
                    try
                    {
                        e.Pointer.Capture(this);
                    }
                    catch { }

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
                    try
                    {
                        e.Pointer.Capture(this);
                    }
                    catch { }

                    e.Handled = true;
                    return;
                }
                else
                {
                    _dragStartPointerX = point.X;
                    _dragOriginalStartBeat = targetClip.StartBeat;
                    _isDragging = true;
                    try
                    {
                        e.Pointer.Capture(this);
                    }
                    catch { }

                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void UpdateGlobalSelection(TimelineEditorViewModel? vm)
    {
        if (vm == null || Application.Current is not App app)
            return;
        app.TimelineContext.SelectedObjects = vm.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>().ToList();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point point = e.GetCurrentPoint(this).Position;
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
            Visual? visualParentForVm = this.GetVisualParent();
            TimelineEditorViewModel? vm = null;
            while (visualParentForVm != null)
            {
                if (visualParentForVm is Control c && c.DataContext is TimelineEditorViewModel t)
                {
                    vm = t;
                    break;
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
                IEnumerable<ClipViewModel> otherClips = vm.Tracks.SelectMany(tr => tr.Clips).Where(o => !_multiDragOriginalStarts.ContainsKey(o));

                var bestStart = SnappingService.ChooseBestStartPreserveEnd(unconstrainedEarliest, unconstrainedLatest, vm, otherClips);
                var bestEnd = SnappingService.ChooseBestEnd(unconstrainedLatest, unconstrainedEarliest, vm, otherClips);

                double startAdjust = bestStart - unconstrainedEarliest;
                double endAdjust = bestEnd - unconstrainedLatest;

                // Choose smaller absolute adjustment (prefer start if equal)
                if (Math.Abs(startAdjust) <= Math.Abs(endAdjust) && Math.Abs(startAdjust) <= vm.SnapThreshold)
                {
                    applyDelta = deltaBeats + startAdjust;
                }
                else if (Math.Abs(endAdjust) < Math.Abs(startAdjust) && Math.Abs(endAdjust) <= vm.SnapThreshold)
                {
                    applyDelta = deltaBeats + endAdjust;
                }
            }

            // Apply delta to all selected clips
            foreach (KeyValuePair<ClipViewModel, double> kv in _multiDragOriginalStarts.ToList())
            {
                kv.Key.StartBeat = kv.Value + applyDelta;
            }

            InvalidateMeasure();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isBoxSelecting)
        {
            _boxCurrentPoint = point;
            InvalidateVisual();
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
            // finalize multi-drag with undo/redo
            if (_multiDragOriginalStarts != null)
            {
                // Find timeline vm
                Visual? visualParentForVm2 = this.GetVisualParent();
                TimelineEditorViewModel? vm2 = null;
                while (visualParentForVm2 != null)
                {
                    if (visualParentForVm2 is Control c && c.DataContext is TimelineEditorViewModel t)
                    {
                        vm2 = t;
                        break;
                    }

                    visualParentForVm2 = visualParentForVm2.GetVisualParent();
                }

                if (vm2 != null)
                {
                    // Build a stable list of (clip, origStart, newStart)
                    var changes = new List<(ClipViewModel Clip, double Orig, double New)>();
                    foreach (var kv in _multiDragOriginalStarts)
                    {
                        changes.Add((kv.Key, kv.Value, kv.Key.StartBeat));
                    }

                    vm2.PushUndo(
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
             }

             _isMultiDragging = false;
             _multiDragOriginalStarts = null;
             try
             {
                 e.Pointer.Capture(null);
             }
             catch { }

             e.Handled = true;
             return;
        }

        if (_isBoxSelecting)
        {
            // finalize selection
            _isBoxSelecting = false;
            try { e.Pointer.Capture(null); } catch { }
            Cursor = new Cursor(StandardCursorType.Arrow);

            Rect selRect = new Rect(Math.Min(_boxStartPoint.X, _boxCurrentPoint.X), Math.Min(_boxStartPoint.Y, _boxCurrentPoint.Y), Math.Abs(_boxCurrentPoint.X - _boxStartPoint.X), Math.Abs(_boxCurrentPoint.Y - _boxStartPoint.Y));

            // If this was just a click (very small drag), treat it as a click: clear selection unless Ctrl pressed
            const double MinDragDistance = 4.0;
            var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

            Visual? visualParentForVm = this.GetVisualParent();
            TimelineEditorViewModel? contextVm = null;
            while (visualParentForVm != null)
            {
                if (visualParentForVm is Control c && c.DataContext is TimelineEditorViewModel t)
                {
                    contextVm = t;
                    break;
                }

                visualParentForVm = visualParentForVm.GetVisualParent();
            }

            // Treat a very small horizontal drag as a click (ignore tiny vertical jitter)
            if (selRect.Width < MinDragDistance)
            {
                 // small click
                 if (!ctrl && contextVm != null)
                 {
                     foreach (ClipViewModel? clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                         clip.IsSelected = false;

                     UpdateGlobalSelection(contextVm);
                     InvalidateVisual();
                 }

                 e.Handled = true;
                 return;
             }

            // Determine which clips intersect selection horizontally (time) and vertically (track area)
            double selStartBeat = (selRect.Left / PixelsPerBeat) + BeatOffset;
            double selEndBeat = (selRect.Right / PixelsPerBeat) + BeatOffset;

            // If no modifier, clear selection first
            if (!ctrl && contextVm != null)
            {
                foreach (ClipViewModel? clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                    clip.IsSelected = false;
            }

            if (contextVm != null)
            {
                // Vertical bounds of clips in this panel
                var bounds = this.Bounds;
                double clipTop = 2.0;
                double clipBottom = Math.Max(1.0, bounds.Height - 2.0);

                // If the selection extends outside this panel vertically, select across all tracks
                bool crossesTracks = selRect.Top < 0 || selRect.Bottom > bounds.Height;

                if (crossesTracks)
                {
                    // Determine which tracks intersect the marquee by using TrackViewModel.Height and ordering
                    var tracks = contextVm.Tracks.ToList();

                    // Find this panel's track index by matching the Clips collection
                    int thisIndex = -1;
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (ReferenceEquals(tracks[i].Clips, Clips) || (Clips != null && tracks[i].Clips.SequenceEqual(Clips)))
                        {
                            thisIndex = i;
                            break;
                        }
                    }

                    if (thisIndex == -1)
                    {
                        // Fallback: select across all tracks if we couldn't find the index
                        foreach (ClipViewModel clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                        {
                            double clipStart = clip.StartBeat;
                            double clipEnd = clip.StartBeat + clip.DurationBeats;
                            if (clipEnd < selStartBeat || clipStart > selEndBeat) continue;
                            clip.IsSelected = true;
                            _lastSelectedClip = clip;
                        }
                    }
                    else
                    {
                        // Compute cumulative top positions for tracks
                        double cum = 0.0;
                        double panelTop = 0.0;
                        for (int i = 0; i < tracks.Count; i++)
                        {
                            if (i == thisIndex)
                                panelTop = cum;
                            cum += tracks[i].Height;
                        }
            
                        double selGlobalTop = panelTop + selRect.Top;
                        double selGlobalBottom = panelTop + selRect.Bottom;
            
                        // For each track, check intersection with global selection and select clips in intersecting tracks
                        double runningTop = 0.0;
                        for (int i = 0; i < tracks.Count; i++)
                        {
                            double trackTop = runningTop;
                            double trackBottom = runningTop + tracks[i].Height;
                            runningTop = trackBottom;
            
                            // Check vertical overlap between track band and selection
                            if (trackBottom < selGlobalTop || trackTop > selGlobalBottom)
                                continue;
            
                            // select clips in this track that overlap horizontally
                            foreach (ClipViewModel clip in tracks[i].Clips)
                            {
                                double clipStart = clip.StartBeat;
                                double clipEnd = clip.StartBeat + clip.DurationBeats;
                                if (clipEnd < selStartBeat || clipStart > selEndBeat) continue;
                                clip.IsSelected = true;
                                _lastSelectedClip = clip;
                            }
                        }
                    }
                }
                else
                {
                    // Iterate only clips that belong to this panel (Clips may be null)
                    if (Clips != null)
                    {
                        foreach (ClipViewModel clip in Clips)
                        {
                            double clipStart = clip.StartBeat;
                            double clipEnd = clip.StartBeat + clip.DurationBeats;

                            // Check horizontal overlap in beats
                            if (clipEnd < selStartBeat || clipStart > selEndBeat)
                                continue;

                            // Check vertical overlap between selection rect and clip vertical band
                            if (selRect.Bottom < clipTop || selRect.Top > clipBottom)
                                continue;

                            clip.IsSelected = true;
                            _lastSelectedClip = clip;
                        }
                    }
                }

                UpdateGlobalSelection(contextVm);
            }

            InvalidateVisual();
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
        _isBoxSelecting = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }
}