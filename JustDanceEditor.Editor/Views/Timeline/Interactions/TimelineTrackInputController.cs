using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Interactions;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline;

internal sealed class TimelineTrackInputController(TimelineTrackPanel owner)
{
    private const double ResizeHitThreshold = 6.0;
    private IEnumerable<ClipViewModel> Clips => owner.Clips;
    private double PixelsPerBeat => owner.PixelsPerBeat;
    private int BeatOffset => owner.BeatOffset;
    private IBrush? Background { get => owner.Background; set => owner.Background = value; }
    private Cursor? Cursor { get => owner.Cursor; set => owner.Cursor = value; }
    private ClipDragHandler? DragHandler => owner.DragHandler;
    private ClipResizeHandler? ResizeHandler => owner.ResizeHandler;
    private BoxSelectionHandler? BoxSelectionHandler => owner.BoxSelectionHandler;
    private TimelineExternalDropController ExternalDropController => owner.ExternalDropController;
    private readonly TimelineTrackContextMenuController _contextMenus = new(owner);
    private ClipViewModel? _lastSelectedClip;

    private TimelineEditorViewModel? GetTimelineVM() => owner.GetTimelineVM();
    private void InvalidateMeasure() => owner.InvalidateMeasure();
    private void InvalidateVisual() => owner.InvalidateVisual();

    private IEnumerable<ClipViewModel> EnumerateClipsTopmostFirst()
    {
        if (Clips == null)
            yield break;

        if (Clips is IList<ClipViewModel> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                yield return list[i];
            yield break;
        }

        List<ClipViewModel> buffered = [.. Clips];
        for (int i = buffered.Count - 1; i >= 0; i--)
            yield return buffered[i];
    }

    private ClipViewModel? FindClipAtPoint(Point point, double ppb, double offset, out double clipStartX, out double clipWidth)
    {
        clipStartX = 0;
        clipWidth = 0;

        if (Clips == null)
            return null;

        foreach (ClipViewModel c in EnumerateClipsTopmostFirst())
        {
            double x = (c.StartBeat - offset) * ppb;
            double w = c.DurationBeats * ppb;
            if (point.X >= x && point.X <= x + w)
            {
                clipStartX = x;
                clipWidth = w;
                return c;
            }
        }

        return null;
    }

    public void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Ensure we have a background so empty-space hits are delivered
        Background ??= Brushes.Transparent;

        Point point = e.GetCurrentPoint(owner).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;
        TimelineEditorViewModel? contextVm = GetTimelineVM();

        ClipViewModel? clickedClip = FindClipAtPoint(point, ppb, offset, out double clickedStartX, out double clickedWidth);

        if (e.GetCurrentPoint(owner).Properties.IsRightButtonPressed && !e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed)
        {
            _contextMenus.Open(e, clickedClip);
            e.Handled = true;
            return;
        }

        // Handle double-click for seeking
        if (e.ClickCount == 2 && clickedClip != null)
        {
            if (contextVm != null)
            {
                double localDoubleClickX = point.X - clickedStartX;
                bool doubleClickNearRight = localDoubleClickX >= (clickedWidth - ResizeHitThreshold);
                if (doubleClickNearRight)
                    contextVm.Playback.SeekToBeat(clickedClip.StartBeat + clickedClip.DurationBeats);
                else
                    contextVm.Playback.SeekToBeat(clickedClip.StartBeat);

                e.Handled = true;
                return;
            }
        }

        if (!e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed)
            return;

        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

        // Empty space: Start box selection
        if (clickedClip == null)
        {
            BoxSelectionHandler?.StartSelection(point, e);
            e.Handled = true;
            return;
        }

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
            DragHandler?.StartMultiDrag(selectedClips, point, e);
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
        double localClipX = point.X - clickedStartX;
        bool nearLeft = localClipX <= ResizeHitThreshold;
        bool nearRight = localClipX >= (clickedWidth - ResizeHitThreshold);
        bool isResizableType = clickedClip.IsResizable;

        if (isResizableType && nearLeft)
        {
            ResizeHandler?.StartResizeLeft(clickedClip, point, e);
            e.Handled = true;
            return;
        }

        if (isResizableType && nearRight)
        {
            ResizeHandler?.StartResizeRight(clickedClip, point, e);
            e.Handled = true;
            return;
        }

        // Default: Start drag
        DragHandler?.StartSingleDrag(clickedClip, point, e);
        e.Handled = true;
    }

    private static void UpdateGlobalSelection(TimelineEditorViewModel? vm)
    {
        if (vm?.Services.TimelineContext == null)
            return;
        vm.Services.TimelineContext.SelectedObjects = [.. vm.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>()];
    }

    public void OnPointerMoved(PointerEventArgs e)
    {
        Point point = e.GetCurrentPoint(owner).Position;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;
        TimelineEditorViewModel? vm = GetTimelineVM();

        // Delegate to active handlers
        if (ResizeHandler?.IsActive == true)
        {
            ResizeHandler.UpdateResize(point, ppb, vm);
            return;
        }

        if (DragHandler?.IsActive == true)
        {
            DragHandler.UpdateDrag(point, ppb, vm);
            InvalidateMeasure();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (BoxSelectionHandler?.IsActive == true)
        {
            BoxSelectionHandler.UpdateSelection(point);
            e.Handled = true;
            return;
        }

        // Update cursor when hovering over clips (for resize)
        TimelineResizeCursor.Update(owner, point, ppb, offset);
    }

    public void OnPointerReleased(PointerReleasedEventArgs e)
    {
        TimelineEditorViewModel? vm = GetTimelineVM();
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

        // Delegate to active handlers
        if (ResizeHandler?.IsActive == true)
        {
            ResizeHandler.Complete(vm, e);
            e.Handled = true;
            return;
        }

        if (DragHandler?.IsActive == true)
        {
            DragHandler.Complete(vm, e);
            e.Handled = true;
            return;
        }

        if (BoxSelectionHandler?.IsActive == true)
        {
            BoxSelectionHandler.Complete(vm, e, addToSelection: ctrl);
            e.Handled = true;
            return;
        }
    }

    public void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Cancel any active interactions in handlers
        DragHandler?.Cancel();
        ResizeHandler?.Cancel();
        BoxSelectionHandler?.Cancel();

        Cursor = new Cursor(StandardCursorType.Arrow);
        ExternalDropController.ClearFeedback();
    }

    public void OpenAddClipMenu(PointerPressedEventArgs? e)
        => _contextMenus.Open(e, clickedClip: null);

}
