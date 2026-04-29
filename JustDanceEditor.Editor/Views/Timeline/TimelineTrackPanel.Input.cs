using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Editor.Views.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Ensure we have a background so empty-space hits are delivered
        Background ??= Brushes.Transparent;

        Point point = e.GetCurrentPoint(this).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;
        TimelineEditorViewModel? contextVm = GetTimelineVM();

        ClipViewModel? clickedClip = FindClipAtPoint(point, ppb, offset, out double clickedStartX, out double clickedWidth);

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            OpenContextMenu(e, clickedClip);
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

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

        // Empty space: Start box selection
        if (clickedClip == null)
        {
            _boxSelectionHandler?.StartSelection(point, e);
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
        double localClipX = point.X - clickedStartX;
        bool nearLeft = localClipX <= ResizeHitThreshold;
        bool nearRight = localClipX >= (clickedWidth - ResizeHitThreshold);
        bool isResizableType = clickedClip.IsResizable;

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

    private static void UpdateGlobalSelection(TimelineEditorViewModel? vm)
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
            _dragHandler.UpdateDrag(point, ppb, vm);
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
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

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

        // Determine track type (DataContext is TrackViewModel)
        TrackViewModel? track = DataContext as TrackViewModel;
        TrackType trackType = track?.TrackType ?? TrackType.Unknown;

        bool valid = false;

        switch (item.Type)
        {
            case ItemType.Pictogram:
                valid = trackType == TrackType.Pictogram;
                break;
            case ItemType.FullBodyMove:
                valid = trackType == TrackType.CoachFullBody;
                break;
            case ItemType.HandMove:
                valid = trackType == TrackType.CoachHand;
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
        TrackType trackType = track?.TrackType ?? TrackType.Unknown;
        bool valid = false;

        switch (item.Type)
        {
            case ItemType.Pictogram:
                valid = trackType == TrackType.Pictogram;
                break;
            case ItemType.FullBodyMove:
                valid = trackType == TrackType.CoachFullBody;
                break;
            case ItemType.HandMove:
                valid = trackType == TrackType.CoachHand;
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

        // Helper functions moved to instance methods - use them to add clips
        Point p = e.GetPosition(this);
        double beat = (p.X / PixelsPerBeat) + BeatOffset;
        beat = SnappingService.FindSnapBeat(beat, vm);

        if (item.Type == ItemType.Pictogram)
        {
            AddPictogramAtBeat(item.Id, beat, track, vm, (int)item.DefaultDuration);
            ClearDragHighlight();
            Cursor = new Cursor(StandardCursorType.Arrow);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (item.Type is ItemType.HandMove or ItemType.FullBodyMove)
        {
            bool isFull = item.Type == ItemType.FullBodyMove;
            AddMoveAtBeat(item.Id, beat, isFull, track, vm, item.DefaultDuration, false);
            ClearDragHighlight();
            Cursor = new Cursor(StandardCursorType.Arrow);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        // Unrecognised item type — reject
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private static void AddPictogramAtBeat(string pictogramId, double dropBeat, TrackViewModel track, TimelineEditorViewModel vm, int durationFrames = 24)
    {
        // Clamp beat to timeline bounds
        double minBeat = vm.TimelineStructure?.StartBeat ?? 0;
        double maxBeat = vm.TimelineStructure?.EndBeat ?? double.MaxValue;
        dropBeat = Math.Max(minBeat, Math.Min(dropBeat, maxBeat - (durationFrames / 24.0)));

        PictogramClip raw = new()
        {
            PictogramId = pictogramId,
            Duration = durationFrames,
            StartTime = (int)(dropBeat * 24.0)
        };

        PictogramClipViewModel clipVm = new(raw, vm.RootPath, vm);

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
    }

    private static void AddMoveAtBeat(string moveId, double dropBeat, bool isFullBody, TrackViewModel track, TimelineEditorViewModel vm, double durationFrames = 24.0, bool isGold = false)
    {
        // Clamp beat to timeline bounds
        double minBeat = vm.TimelineStructure?.StartBeat ?? 0;
        double maxBeat = vm.TimelineStructure?.EndBeat ?? double.MaxValue;
        dropBeat = Math.Max(minBeat, Math.Min(dropBeat, maxBeat - durationFrames));

        MoveClip raw = new()
        {
            MoveId = moveId,
            StartTime = (int)(dropBeat * 24.0),
            IsGoldMove = isGold
        };

        MoveClipViewModel clipVm = new(raw, vm.RootPath, vm, isFullBody, (int)durationFrames);

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
    }

    public static ContextMenu? CurrentContextMenu { get; private set; }

    private static void AddMenuItem(ContextMenu menu, MenuItem item)
    {
        if (menu.Items is System.Collections.IList list)
            list.Add(item);
    }

    private static void AddSeparator(ContextMenu menu)
    {
        if (menu.Items is System.Collections.IList list)
            list.Add(new Separator());
    }

    private void OpenContextMenu(PointerPressedEventArgs? e, ClipViewModel? clickedClip)
    {
        CurrentContextMenu?.Close();

        ContextMenu menu = new();
        TimelineEditorViewModel? timeline = GetTimelineVM();
        TrackViewModel? track = DataContext as TrackViewModel;
        if (track == null || timeline == null)
        {
            MenuItem addFallback = new() { Header = "Add Clip" };
            addFallback.Click += async (_, _) => await ShowCreateClipMenuAsync(e);
            AddMenuItem(menu, addFallback);

            CurrentContextMenu = menu;
            menu.Closed += (_, _) =>
            {
                if (CurrentContextMenu == menu)
                    CurrentContextMenu = null;
            };

            if (Application.Current != null)
                menu.Open(this);

            return;
        }

        bool isPictogramTrack = track.TrackType == TrackType.Pictogram || string.Equals(track.Title, "Pictograms", StringComparison.OrdinalIgnoreCase);

        if (isPictogramTrack && clickedClip is PictogramClipViewModel pictogramClip)
        {
            MenuItem selectAll = new() { Header = "Select All Instances" };
            selectAll.Click += (_, _) => SelectAllPictogramInstances(timeline, pictogramClip);
            AddMenuItem(menu, selectAll);

            AddSeparator(menu);
            MenuItem regenerate = new() { Header = "Regenerate Pictogram" };
            regenerate.Click += async (_, _) => await RegeneratePictogramFromMenuAsync(timeline, pictogramClip);
            AddMenuItem(menu, regenerate);

            MenuItem flip = new() { Header = "Flip" };
            flip.Click += async (_, _) => await FlipSelectedPictogramsAsync(timeline);
            AddMenuItem(menu, flip);
        }
        else if (clickedClip is MoveClipViewModel moveClip)
        {
            MenuItem selectAll = new() { Header = "Select All Instances" };
            selectAll.Click += (_, _) => SelectAllMoveInstances(timeline, moveClip);
            AddMenuItem(menu, selectAll);
        }
        else
        {
            MenuItem add = new() { Header = "Add Clip" };
            add.Click += async (_, _) => await ShowCreateClipMenuAsync(e);
            AddMenuItem(menu, add);

            if (isPictogramTrack)
            {
                AddSeparator(menu);
                MenuItem generate = new() { Header = "Generate New Pictogram" };
                generate.Click += async (_, _) => await GenerateNewPictogramFromMenuAsync(timeline);
                AddMenuItem(menu, generate);
            }
        }

        CurrentContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (CurrentContextMenu == menu)
                CurrentContextMenu = null;
        };

        if (Application.Current != null)
            menu.Open(this);
    }

    private void SelectAllPictogramInstances(TimelineEditorViewModel timeline, PictogramClipViewModel clip)
    {
        _ = timeline.SelectAllPictogramInstances(clip.PictogramId);
        UpdateGlobalSelection(timeline);
        InvalidateVisual();
    }

    private void SelectAllMoveInstances(TimelineEditorViewModel timeline, MoveClipViewModel clip)
    {
        _ = timeline.SelectAllMoveInstances(clip.MoveId);
        UpdateGlobalSelection(timeline);
        InvalidateVisual();
    }

    private static async Task FlipSelectedPictogramsAsync(TimelineEditorViewModel timeline)
    {
        // Collect all selected pictogram clips
        List<PictogramClipViewModel> selectedPictograms = [.. timeline.Tracks
            .SelectMany(t => t.Clips)
            .OfType<PictogramClipViewModel>()
            .Where(c => c.IsSelected)];

        // Flip each selected pictogram individually
        foreach (var clip in selectedPictograms)
        {
            await timeline.FlipPictogramAsync(clip.PictogramId, clip);
        }
    }

    /// <summary>
    /// Opens a minimal context menu offering the Add Clip command.  Previous
    /// global menus are closed first.  This helper exists to simplify testing.
    /// </summary>
    public void OpenAddClipMenu(PointerPressedEventArgs? e)
    {
        OpenContextMenu(e, clickedClip: null);
    }

    private static IReadOnlyList<PictogramReferenceMoveOption> BuildReferenceMoveOptions(TimelineEditorViewModel timeline)
    {
        const double selectionWindowSeconds = 5.0;
        double playheadSeconds = timeline.GetPlaybackSecondsAtBeatLabel(timeline.CurrentBeat);

        var options = timeline.Tracks
            .Where(t => t.TrackType is TrackType.CoachHand or TrackType.CoachFullBody)
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()
            .Where(c => !string.IsNullOrWhiteSpace(c.MoveId))
            .GroupBy(c => (c.MoveId, c.RawClip.StartTime))
            .Select(g => new PictogramReferenceMoveOption
            {
                MoveId = g.Key.MoveId,
                StartFrame = g.Key.StartTime
            })
            .Select(o => new
            {
                Option = o,
                DistanceSeconds = Math.Abs(timeline.GetPlaybackSecondsAtBeatLabel(o.StartBeat) - playheadSeconds)
            })
            .Where(x => x.DistanceSeconds <= selectionWindowSeconds)
            .OrderBy(x => x.DistanceSeconds)
            .ThenBy(x => x.Option.StartFrame);

        return [.. options.Select(x => x.Option)];
    }

    private static async Task GenerateNewPictogramFromMenuAsync(TimelineEditorViewModel timeline)
    {
        if (Application.Current is not App app)
            return;

        PictogramScreenshotOptionsViewModel optionsVm = new();
        optionsVm.EnableInsertionOptions(BuildReferenceMoveOptions(timeline));

        PictogramScreenshotOptionsResult? result = await app.DialogService.ShowDialogAsync<PictogramScreenshotOptionsResult>(optionsVm);
        if (result == null)
            return;

        await timeline.GenerateNewPictogramAsync(timeline.CurrentBeat, result);
    }

    private static async Task RegeneratePictogramFromMenuAsync(TimelineEditorViewModel timeline, PictogramClipViewModel clip)
    {
        if (Application.Current is not App app)
            return;

        PictogramScreenshotOptionsViewModel optionsVm = new();
        PictogramScreenshotOptionsResult? result = await app.DialogService.ShowDialogAsync<PictogramScreenshotOptionsResult>(optionsVm);
        if (result == null)
            return;

        await timeline.RegeneratePictogramAsync(clip, result);
    }

    private async Task ShowCreateClipMenuAsync(PointerPressedEventArgs? e)
    {
        // Compute beat at pointer
        Point p = e?.GetPosition(this) ?? default;
        double beat = (p.X / PixelsPerBeat) + BeatOffset;
        TimelineEditorViewModel? vm = GetTimelineVM();
        if (DataContext is not TrackViewModel track || vm == null)
            return;

        // Determine options based on track title
        string title = track.Title;

        if (string.Equals(title, "Pictograms", StringComparison.OrdinalIgnoreCase))
        {
            PictogramCreationViewModel picDialogVm = new(vm.AvailablePictograms, vm.RootPath);
            if (Application.Current is not App app)
                return;

            PictogramCreationResult? picRes = await app.DialogService.ShowDialogAsync<PictogramCreationResult>(picDialogVm);
            if (picRes != null)
                AddPictogramAtBeat(picRes.PictogramId, SnappingService.FindSnapBeat(beat, vm), track, vm, picRes.Frames);

            return;
        }

        if (title.Contains("Coach", StringComparison.OrdinalIgnoreCase))
        {
            bool isFull = title.Contains("FullBody", StringComparison.OrdinalIgnoreCase);
            IEnumerable<string> moves = isFull ? vm.AvailableFullBodyCoachMoves : vm.AvailableHandCoachMoves;
            MoveCreationViewModel moveDialogVm = new(moves, isFull);
            if (Application.Current is not App app)
                return;

            MoveCreationResult? moveRes = await app.DialogService.ShowDialogAsync<MoveCreationResult>(moveDialogVm);
            if (moveRes != null)
                AddMoveAtBeat(moveRes.MoveId, SnappingService.FindSnapBeat(beat, vm), isFull, track, vm, moveRes.Frames, moveRes.IsGold);

            return;
        }

        if (string.Equals(title, "Hide HUD", StringComparison.OrdinalIgnoreCase))
        {
            if (Application.Current is not App app)
                return;

            HideHudCreationViewModel dialogVm = new();
            HideHudCreationResult? res = await app.DialogService.ShowDialogAsync<HideHudCreationResult>(dialogVm);
            if (res != null)
            {
                // Clamp beat to timeline bounds
                double minBeat = vm.TimelineStructure?.StartBeat ?? 0;
                double maxBeat = vm.TimelineStructure?.EndBeat ?? double.MaxValue;
                double clampedBeat = Math.Max(minBeat, Math.Min(SnappingService.FindSnapBeat(beat, vm), maxBeat - (res.Frames / 24.0)));

                HideUserInterfaceClip raw = new()
                {
                    StartTime = (int)(clampedBeat * 24.0),
                    Duration = res.Frames,
                    IsActive = true
                };

                HideUserInterfaceClipViewModel clipVm = new(raw, vm.RootPath, vm);

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
            }

            return;
        }

        if (string.Equals(title, "Lyrics", StringComparison.OrdinalIgnoreCase))
        {
            LyricsCreationViewModel dialogVm = new();
            if (Application.Current is not App app)
                return;

            LyricsCreationResult? res = await app.DialogService.ShowDialogAsync<LyricsCreationResult>(dialogVm);

            if (res != null)
            {
                // Clamp beat to timeline bounds
                double minBeat = vm.TimelineStructure?.StartBeat ?? 0;
                double maxBeat = vm.TimelineStructure?.EndBeat ?? double.MaxValue;
                double clampedBeat = Math.Max(minBeat, Math.Min(SnappingService.FindSnapBeat(beat, vm), maxBeat - (res.Frames / 24.0)));

                KaraokeClip raw = new()
                {
                    Lyrics = res.Lyrics,
                    Duration = res.Frames,
                    IsEndOfLine = res.IsEndOfLine,
                    StartTime = (int)(clampedBeat * 24.0)
                };

                KaraokeClipViewModel clipVm = new(raw, vm.RootPath, vm);

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
            }

            return;
        }

        if (string.Equals(title, "Gold Effects", StringComparison.OrdinalIgnoreCase))
        {
            GoldEffectCreationViewModel dialogVm = new();
            if (Application.Current is not App app)
                return;

            GoldEffectCreationResult? res = await app.DialogService.ShowDialogAsync<GoldEffectCreationResult>(dialogVm);
            if (res != null)
            {
                // Clamp beat to timeline bounds
                double minBeat = vm.TimelineStructure?.StartBeat ?? 0;
                double maxBeat = vm.TimelineStructure?.EndBeat ?? double.MaxValue;
                double clampedBeat = Math.Max(minBeat, Math.Min(SnappingService.FindSnapBeat(beat, vm), maxBeat - (res.Frames / 24.0)));

                GoldEffectClip raw = new()
                {
                    Duration = res.Frames,
                    EffectType = res.EffectType,
                    StartTime = (int)(clampedBeat * 24.0)
                };

                GoldEffectClipViewModel clipVm = new(raw, vm.RootPath, vm);

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
            }

            return;
        }

        // Default: show simple menu with a 'Create Empty Karaoke Clip' fallback
        string? fallback = await ShowInputDialogAsync("Enter lyrics text (empty to create placeholder):");
        if (fallback != null && vm != null)
        {
            // Clamp beat to timeline bounds
            double minBeat = vm.TimelineStructure?.StartBeat ?? 0;
            double maxBeat = vm.TimelineStructure?.EndBeat ?? double.MaxValue;
            double clampedBeat = Math.Max(minBeat, Math.Min(SnappingService.FindSnapBeat(beat, vm), maxBeat - (24.0 / 24.0)));

            KaraokeClip raw = new()
            {
                Lyrics = fallback,
                Duration = 24,
                StartTime = (int)(clampedBeat * 24.0)
            };

            KaraokeClipViewModel clipVm = new(raw, vm.RootPath, vm);

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
        }
    }

    private async Task<string?> ShowInputDialogAsync(string prompt)
    {
        Window? owner = this.GetVisualRoot() as Window;
        Window win = new()
        {
            Title = prompt,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 320,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(6) }
        };

        if (win.Content is not StackPanel stack)
            throw new InvalidOperationException("Input dialog content was not initialized correctly.");
        TextBox box = new() { Width = 440 };
        stack.Children.Add(box);
        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        stack.Children.Add(footer);

        string? result = null;
        ok.Click += (s, e) =>
        {
            result = box.Text;
            win.Close();
        };
        cancel.Click += (s, e) => win.Close();

        Window ownerWindow = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null) ?? throw new InvalidOperationException("No owner window available");
        await win.ShowDialog(ownerWindow);
        return result;
    }
}