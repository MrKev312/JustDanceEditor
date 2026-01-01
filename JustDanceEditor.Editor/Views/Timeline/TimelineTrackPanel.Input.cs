using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Interactions;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;

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
        TimelineEditorViewModel? contextVm = GetTimelineVM();

        // Handle double-click for seeking
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

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Find clicked clip
        ClipViewModel? clickedClip = null;
        double clickedStartX = 0, clickedWidth = 0;

        if (Clips != null)
        {
            foreach (ClipViewModel c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                if (point.X >= x && point.X <= x + w)
                {
                    clickedClip = c;
                    clickedStartX = x;
                    clickedWidth = w;
                    break;
                }
            }
        }

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

        // Empty space: Start box selection
        if (clickedClip == null)
        {
            _boxSelectionHandler?.StartSelection(point, e);
            e.Handled = true;
            return;
        }

        // Clicked clip: Handle selection
        if (clickedClip != null)
        {
            // Ctrl: Toggle selection
            if (ctrl && !shift)
            {
                clickedClip.IsSelected = !clickedClip.IsSelected;
                _lastSelectedClip = clickedClip.IsSelected ? clickedClip : _lastSelectedClip;
                UpdateGlobalSelection(contextVm);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Shift: Range selection
            if (shift && contextVm != null)
            {
                var allClips = contextVm.Tracks
                    .SelectMany(tr => tr.Clips.OrderBy(c => c.StartBeat))
                    .ToList();

                int a = allClips.IndexOf(_lastSelectedClip ?? clickedClip);
                int b = allClips.IndexOf(clickedClip);

                if (a != -1 && b != -1)
                {
                    int start = Math.Min(a, b);
                    int end = Math.Max(a, b);
                    for (int i = 0; i < allClips.Count; i++)
                        allClips[i].IsSelected = i >= start && i <= end;
                }

                _lastSelectedClip = clickedClip;
                UpdateGlobalSelection(contextVm);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Multi-drag: Click on selected clip when multiple are selected
            List<ClipViewModel> selectedClips = (contextVm != null) ?
                contextVm.Tracks.SelectMany(tr => tr.Clips).Where(c => c.IsSelected).ToList() :
                Clips?.Where(c => c.IsSelected).ToList() ?? [];

            if (selectedClips.Count > 1 && selectedClips.Contains(clickedClip))
            {
                _dragHandler?.StartMultiDrag(selectedClips, point, e);
                e.Handled = true;
                return;
            }

            // Normal selection: Make this clip the only selected
            if (!ctrl && !shift && contextVm != null)
            {
                foreach (ClipViewModel clip in contextVm.Tracks.SelectMany(tr => tr.Clips))
                    clip.IsSelected = false;
                clickedClip.IsSelected = true;
                _lastSelectedClip = clickedClip;
                UpdateGlobalSelection(contextVm);
            }

            // Check for resize vs drag
            double localX = point.X - clickedStartX;
            bool nearLeft = localX <= ResizeHitThreshold;
            bool nearRight = localX >= (clickedWidth - ResizeHitThreshold);
            bool isResizableType = clickedClip.RawClip is PictogramClip or KaraokeClip;

            if (isResizableType && nearLeft)
            {
                _resizeHandler?.StartResizeLeft(clickedClip, point, e);
                e.Handled = true;
                return;
            }

            if (isResizableType && nearRight)
            {
                _resizeHandler?.StartResizeRight(clickedClip, point, e);
                e.Handled = true;
                return;
            }

            // Default: Start drag
            _dragHandler?.StartSingleDrag(clickedClip, point, e);
            e.Handled = true;
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
        int offset = BeatOffset;
        TimelineEditorViewModel? vm = GetTimelineVM();

        // Delegate to active handlers
        if (_resizeHandler?.IsActive == true)
        {
            _resizeHandler.UpdateResize(point, ppb);
            return;
        }

        if (_dragHandler?.IsActive == true)
        {
            _dragHandler.UpdateDrag(point, ppb, offset, vm);
            InvalidateMeasure();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_boxSelectionHandler?.IsActive == true)
        {
            _boxSelectionHandler.UpdateSelection(point);
            e.Handled = true;
            return;
        }

        // Update cursor when hovering over clips (for resize)
        UpdateResizeCursor(point, ppb, offset);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        TimelineEditorViewModel? vm = GetTimelineVM();
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

        // Delegate to active handlers
        if (_resizeHandler?.IsActive == true)
        {
            _resizeHandler.Complete(vm, e);
            e.Handled = true;
            return;
        }

        if (_dragHandler?.IsActive == true)
        {
            _dragHandler.Complete(vm, e);
            e.Handled = true;
            return;
        }

        if (_boxSelectionHandler?.IsActive == true)
        {
            _boxSelectionHandler.Complete(vm, e, addToSelection: ctrl);
            e.Handled = true;
            return;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // Cancel any active interactions in handlers
        _dragHandler?.Cancel();
        _resizeHandler?.Cancel();
        _boxSelectionHandler?.Cancel();

        Cursor = new Cursor(StandardCursorType.Arrow);
    }
}