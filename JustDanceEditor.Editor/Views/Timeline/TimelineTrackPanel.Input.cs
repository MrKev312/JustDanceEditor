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
                double localDoubleClickX = point.X - targetStartX;
                bool doubleClickNearRight = localDoubleClickX >= (targetWidth - ResizeHitThreshold);
                if (doubleClickNearRight)
                    contextVm.Playback.SeekToBeat(targetClip.StartBeat + targetClip.DurationBeats);
                else
                    contextVm.Playback.SeekToBeat(targetClip.StartBeat);

                e.Handled = true;
                return;
            }
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Right-click context: offer quick 'Create Clip' actions via a context menu
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                ContextMenu menu = new();
                MenuItem add = new() { Header = "Add Clip" };
                add.Click += async (s, args) => await ShowCreateClipMenuAsync(e);
                // Add item to the menu's items collection (Items has no setter)
                if (menu.Items is System.Collections.IList list)
                    list.Add(add);
                menu.Open(this);
                e.Handled = true;
            }

            return;
        }

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

        // Validate same rules as OnDragOver
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
    }

    private static void AddPictogramAtBeat(string pictogramId, double dropBeat, TrackViewModel track, TimelineEditorViewModel vm, int durationFrames = 24)
    {
        PictogramClip raw = new()
        {
            PictogramId = pictogramId,
            Duration = durationFrames,
            StartTime = (int)(dropBeat * 24.0)
        };

        PictogramClipViewModel clipVm = new(raw, raw.Duration, Colors.LightBlue, raw.PictogramId, vm.RootPath, vm);

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
        MoveClip raw = new()
        {
            MoveId = moveId,
            StartTime = (int)(dropBeat * 24.0),
            IsGoldMove = isGold
        };

        Color moveColor = Colors.LightGray;
        if (vm.TryGetCoachMoveColor(moveId, out Color c))
            moveColor = c;

        MoveClipViewModel clipVm = new(raw, durationFrames, moveColor, moveId, vm.RootPath, vm, isFullBody);

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

    private async Task ShowCreateClipMenuAsync(PointerPressedEventArgs e)
    {
        // Compute beat at pointer
        Point p = e.GetPosition(this);
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

        if (string.Equals(title, "Lyrics", StringComparison.OrdinalIgnoreCase))
        {
            LyricsCreationViewModel dialogVm = new();
            if (Application.Current is not App app)
                return;

            LyricsCreationResult? res = await app.DialogService.ShowDialogAsync<LyricsCreationResult>(dialogVm);

            if (res != null)
            {
                KaraokeClip raw = new()
                {
                    Lyrics = res.Lyrics,
                    Duration = res.Frames,
                    IsEndOfLine = res.IsEndOfLine,
                    StartTime = (int)(SnappingService.FindSnapBeat(beat, vm) * 24.0)
                };

                KaraokeClipViewModel clipVm = new(raw, raw.Duration, Colors.Goldenrod, raw.Lyrics, vm.RootPath, vm);

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
                GoldEffectClip raw = new()
                {
                    Duration = res.Frames,
                    EffectType = res.EffectType,
                    StartTime = (int)(SnappingService.FindSnapBeat(beat, vm) * 24.0)
                };

                GoldEffectClipViewModel clipVm = new(raw, raw.Duration, Colors.Gold, "Gold Effect", vm.RootPath, vm);

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
            KaraokeClip raw = new()
            {
                Lyrics = fallback,
                Duration = 24,
                StartTime = (int)(SnappingService.FindSnapBeat(beat, vm) * 24.0)
            };

            KaraokeClipViewModel clipVm = new(raw, raw.Duration, Colors.Goldenrod, raw.Lyrics, vm.RootPath, vm);

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

    private async Task<LibraryItemViewModel?> ShowLibraryPickerAsync(ItemType desired)
    {
        // Build picker window with LibraryToolView
        Window? owner = this.GetVisualRoot() as Window;
        Window win = new()
        {
            Title = "Choose Library Item",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel()
        };

        StackPanel stack = (StackPanel)win.Content!;
        LibraryToolView libView = new()
        {
            DataContext = new LibraryToolViewModel()
        };
        stack.Children.Add(libView);

        // Footer buttons
        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        stack.Children.Add(footer);

        LibraryItemViewModel? result = null;

        ok.Click += (s, e) =>
        {
            // Find selected item in appropriate grid
            DataGrid? dg = null;
            switch (desired)
            {
                case ItemType.Pictogram: dg = libView.FindControl<DataGrid>("ItemsListPictograms"); break;
                case ItemType.HandMove: dg = libView.FindControl<DataGrid>("ItemsListHandMoves"); break;
                case ItemType.FullBodyMove: dg = libView.FindControl<DataGrid>("ItemsListFullBody"); break;
            }

            if (dg != null)
                result = dg.SelectedItem as LibraryItemViewModel;

            win.Close();
        };

        cancel.Click += (s, e) => win.Close();

        Window ownerWindow = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null) ?? throw new InvalidOperationException("No owner window available");
        await win.ShowDialog(ownerWindow);
        return result;
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

        StackPanel stack = (StackPanel)win.Content!;
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