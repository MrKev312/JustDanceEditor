using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Editor.Views.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline;

// Helper fields for drag highlight
partial class TimelineTrackPanel
{
    private IBrush? _originalBackgroundBrush;
    private bool _isDragHighlightActive = false;

    private void SetDragHighlight(Color c)
    {
        try
        {
            if (!_isDragHighlightActive)
            {
                _originalBackgroundBrush = Background;
                _isDragHighlightActive = true;
            }

            Background = new SolidColorBrush(c) { Opacity = 0.2 };
        }
        catch { }
    }

    private void ClearDragHighlight()
    {
        try
        {
            if (_isDragHighlightActive)
            {
                Background = _originalBackgroundBrush;
                _isDragHighlightActive = false;
            }
        }
        catch { }
    }
}

public partial class TimelineTrackPanel
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Ensure we have a background so empty-space hits are delivered
        Background ??= Brushes.Transparent;

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
                List<ClipViewModel> allClips = [.. contextVm.Tracks.SelectMany(tr => tr.Clips.OrderBy(c => c.StartBeat))];

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
                [.. contextVm.Tracks.SelectMany(tr => tr.Clips).Where(c => c.IsSelected)] :
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
            bool isResizableType = clickedClip is PictogramClipViewModel or KaraokeClipViewModel or MoveClipViewModel;

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
        app.TimelineContext.SelectedObjects = [.. vm.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>()];
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
            _resizeHandler.UpdateResize(point, ppb, vm);
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
        ClearDragHighlight();
    }

    private void OnExternalDragOver(object? sender, DragEventArgs e)
    {
        // Check if this is a library item drag by looking at the static context
        LibraryItemViewModel? item = LibraryToolView.GetDraggedItem();

        if (item == null)
        {
            // Not a library drag
            ClearDragHighlight();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Determine track title (DataContext is TrackViewModel)
        TrackViewModel? track = DataContext as TrackViewModel;
        string title = track?.Title ?? string.Empty;

        bool valid = false;

        switch (item.Type)
        {
            case ItemType.Pictogram:
                valid = string.Equals(title, "Pictograms", StringComparison.OrdinalIgnoreCase);
                break;
            case ItemType.FullBodyMove:
                valid = title.Contains("FullBody", StringComparison.OrdinalIgnoreCase);
                break;
            case ItemType.HandMove:
                valid = title.Contains("Coach", StringComparison.OrdinalIgnoreCase) && title.IndexOf("FullBody", StringComparison.OrdinalIgnoreCase) < 0;
                break;
        }

        // Visual feedback: highlight track and set cursor
        if (valid)
        {
            SetDragHighlight(Colors.ForestGreen);
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            SetDragHighlight(Colors.DarkRed);
            Cursor = new Cursor(StandardCursorType.No);
        }

        e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnExternalDragEnter(object? sender, DragEventArgs e)
    {
        LibraryItemViewModel? item = LibraryToolView.GetDraggedItem();
        if (item == null)
            return;

        TrackViewModel? track = DataContext as TrackViewModel;
        string title = track?.Title ?? string.Empty;
        bool valid = false;

        switch (item.Type)
        {
            case ItemType.Pictogram:
                valid = string.Equals(title, "Pictograms", StringComparison.OrdinalIgnoreCase);
                break;
            case ItemType.FullBodyMove:
                valid = title.Contains("FullBody", StringComparison.OrdinalIgnoreCase);
                break;
            case ItemType.HandMove:
                valid = title.Contains("Coach", StringComparison.OrdinalIgnoreCase) && title.IndexOf("FullBody", StringComparison.OrdinalIgnoreCase) < 0;
                break;
        }

        if (valid)
        {
            SetDragHighlight(Colors.ForestGreen);
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            SetDragHighlight(Colors.DarkRed);
            Cursor = new Cursor(StandardCursorType.No);
        }

        e.Handled = true;
    }

    private void OnExternalDragLeave(object? sender, DragEventArgs e)
    {
        ClearDragHighlight();
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Handled = true;
    }

    private void OnExternalDrop(object? sender, DragEventArgs e)
    {
        // Clear highlight on drop
        ClearDragHighlight();
        Cursor = new Cursor(StandardCursorType.Arrow);

        LibraryItemViewModel? item = LibraryToolView.GetDraggedItem();
        if (item == null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        TimelineEditorViewModel? vm = GetTimelineVM();
        if (DataContext is not TrackViewModel track || vm == null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Validate same rules as OnDragOver
        string title = track.Title;
        bool valid = false;

        switch (item.Type)
        {
            case ItemType.Pictogram:
                valid = string.Equals(title, "Pictograms", StringComparison.OrdinalIgnoreCase);
                break;
            case ItemType.FullBodyMove:
                valid = title.Contains("FullBody", StringComparison.OrdinalIgnoreCase);
                break;
            case ItemType.HandMove:
                valid = title.Contains("Coach", StringComparison.OrdinalIgnoreCase) && title.IndexOf("FullBody", StringComparison.OrdinalIgnoreCase) < 0;
                break;
        }

        if (!valid)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Compute beat at drop position
        Point p = e.GetPosition(this);
        double beat = (p.X / PixelsPerBeat) + BeatOffset;
        beat = SnappingService.FindSnapBeat(beat, vm);

        // Create raw clip and clip view model
        if (item.Type == ItemType.Pictogram)
        {

            PictogramClip raw = new()
            {
                PictogramId = item.Id,
                Duration = (int)item.DefaultDuration,
                StartTime = (int)(beat * 24.0)
            };

            PictogramClipViewModel clipVm = new(raw, raw.Duration, Colors.LightBlue, raw.PictogramId, vm.RootPath, vm);

            // Push undo/redo
            vm.PushUndo(
                undo: () =>
                {
                    if (track.Clips.Contains(clipVm))
                        track.Clips.Remove(clipVm);
                },
                redo: () =>
                {
                    if (!track.Clips.Contains(clipVm))
                        track.Clips.Add(clipVm);
                }
            );

            // Execute
            track.Clips.Add(clipVm);

            // reset visual state and cursor to default
            ClearDragHighlight();
            Cursor = new Cursor(StandardCursorType.Arrow);

            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        // Moves
        if (item.Type is ItemType.HandMove or ItemType.FullBodyMove)
        {

            MoveClip raw = new()
            {
                MoveId = item.Id,
                StartTime = (int)(beat * 24.0)
            };

            // Color
            Color moveColor = Colors.LightGray;
            if (vm.TryGetCoachMoveColor(item.Id, out Color c))
                moveColor = c;

            bool isFullBody = item.Type == ItemType.FullBodyMove;

            MoveClipViewModel clipVm = new(raw, item.DefaultDuration, moveColor, item.Id, vm.RootPath, vm, isFullBody);

            vm.PushUndo(
                undo: () =>
                {
                    if (track.Clips.Contains(clipVm))
                        track.Clips.Remove(clipVm);
                },
                redo: () =>
                {
                    if (!track.Clips.Contains(clipVm))
                        track.Clips.Add(clipVm);
                }
            );

            track.Clips.Add(clipVm);

            // reset visual state and cursor to default
            ClearDragHighlight();
            Cursor = new Cursor(StandardCursorType.Arrow);

            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
    }
}