using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;

using System;
using System.Threading;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class LibraryToolView : UserControl
{
    private Point _pressPoint;
    private LibraryItemViewModel? _pressItem;
    private PointerPressedEventArgs? _pressEventArgs;
    private const double DragThreshold = 4.0;

    // Static context for passing library items during drag-drop (avoids obsolete DataObject)
    private static LibraryItemViewModel? _draggedItem;
    private static readonly Lock _dragContextLock = new();
    private static ContextMenu? _currentContextMenu;

    public LibraryToolView()
    {
        InitializeComponent();

        // Attach pointer handlers to each tab's DataGrid so drag works regardless of active tab
        foreach (string? name in new[] { "ItemsListPictograms", "ItemsListHandMoves", "ItemsListFullBody" })
        {
            DataGrid? grid = this.FindControl<DataGrid>(name);
            if (grid != null)
            {
                // Use AddHandler with handledEventsToo so our handlers run even when DataGrid processes the events
                grid.AddHandler(PointerPressedEvent, Items_PointerPressed, handledEventsToo: true);
                grid.AddHandler(PointerMovedEvent, Items_PointerMoved, handledEventsToo: true);
                grid.AddHandler(PointerReleasedEvent, Items_PointerReleased, handledEventsToo: true);
            }
        }
    }

    private void Items_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not LibraryToolViewModel)
            return;

        if (sender is not DataGrid grid)
            return;

        LibraryItemViewModel? hit = TryGetItemAtPointer(e, grid);

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (hit != null)
            {
                OpenLibraryContextMenu(grid, hit);
                e.Handled = true;
            }

            return;
        }

        _pressPoint = e.GetCurrentPoint(this).Position;

        _pressItem = hit;
        _pressEventArgs = hit != null ? e : null;
        // store candidate for drag initiation
        if (_pressItem != null)
        {
            // nothing else required here
        }
    }

    private static LibraryItemViewModel? TryGetItemAtPointer(PointerEventArgs e, DataGrid grid)
    {
        if (e.Source is Visual v)
        {
            Visual? cur = v;
            while (cur is not null and not DataGridRow)
                cur = cur.GetVisualParent();
            if (cur is DataGridRow row)
                return row.DataContext as LibraryItemViewModel;
        }

        if (grid.SelectedItem is LibraryItemViewModel selected)
            return selected;

        return null;
    }

    private void OpenLibraryContextMenu(Control placementTarget, LibraryItemViewModel item)
    {
        _currentContextMenu?.Close();

        ContextMenu menu = new();
        MenuItem rename = new() { Header = "Rename" };
        rename.Click += async (_, _) => await RenameLibraryItemAsync(item);
        if (menu.Items is System.Collections.IList list)
            list.Add(rename);

        if (item.Type == ItemType.Pictogram)
        {
            MenuItem flip = new() { Header = "Flip" };
            flip.Click += async (_, _) =>
            {
                if (DataContext is LibraryToolViewModel libVm && libVm.ActiveTimeline is TimelineEditorViewModel timeline)
                {
                    bool ok = await timeline.FlipPictogramAsync(item.Id);
                    if (ok)
                    {
                        // Toggle displayed id
                        if (item.Id.EndsWith("_flipped", StringComparison.OrdinalIgnoreCase))
                            item.Id = item.Id[..^"_flipped".Length];
                        else
                            item.Id += "_flipped";

                        item.Name = item.Id;
                    }
                }
            };

            if (menu.Items is System.Collections.IList list2)
                list2.Add(flip);
        }

        _currentContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (_currentContextMenu == menu)
                _currentContextMenu = null;
        };

        menu.Open(placementTarget);
    }

    private async System.Threading.Tasks.Task RenameLibraryItemAsync(LibraryItemViewModel item)
    {
        if (DataContext is not LibraryToolViewModel libVm || libVm.ActiveTimeline is not TimelineEditorViewModel timeline)
            return;

        string title = item.Type == ItemType.Pictogram ? "Rename Pictogram" : "Rename Move";
        string? newName = await ShowRenameDialogAsync(title, item.Id);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        newName = newName.Trim();
        if (string.Equals(item.Id, newName, StringComparison.OrdinalIgnoreCase))
            return;

        bool ok = item.Type switch
        {
            ItemType.Pictogram => timeline.RenamePictogramId(item.Id, newName),
            ItemType.HandMove => timeline.RenameMoveId(item.Id, newName, isFullBody: false),
            ItemType.FullBodyMove => timeline.RenameMoveId(item.Id, newName, isFullBody: true),
            _ => false
        };

        if (!ok)
            return;

        item.Id = newName;
        item.Name = newName;
    }

    private async System.Threading.Tasks.Task<string?> ShowRenameDialogAsync(string title, string currentName)
    {
        Window? owner = TopLevel.GetTopLevel(this) as Window;
        Window win = new()
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 320,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(6) }
        };

        if (win.Content is not StackPanel stack)
            throw new InvalidOperationException("Rename dialog content was not initialized correctly.");

        TextBox box = new() { Width = 440, Text = currentName };
        stack.Children.Add(box);

        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        stack.Children.Add(footer);

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = box.Text;
            win.Close();
        };
        cancel.Click += (_, _) => win.Close();

        Window ownerWindow = owner ??
            (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw
                ? mw
                : null)
            ?? throw new InvalidOperationException("No owner window available");

        await win.ShowDialog(ownerWindow);
        return result;
    }

    private async void Items_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressItem == null)
            return;

        Point p = e.GetCurrentPoint(this).Position;
        double dx = Math.Abs(p.X - _pressPoint.X);
        double dy = Math.Abs(p.Y - _pressPoint.Y);

        if ((dx > DragThreshold || dy > DragThreshold) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Store the item in static context to be retrieved by drop handlers
            lock (_dragContextLock)
            {
                _draggedItem = _pressItem;
            }

            // Provide immediate cursor feedback while drag is active
            Cursor? prevCursor = Cursor;
            Cursor = new Cursor(StandardCursorType.Hand);

            DataTransfer data = new();
            data.Add(DataTransferItem.CreateText("LibraryItem"));
            try
            {
                if (_pressEventArgs != null)
                    await DragDrop.DoDragDropAsync(_pressEventArgs, data, DragDropEffects.Copy);
            }
            catch (Exception)
            {
                // swallow exceptions from DoDragDrop
            }
            finally
            {
                // Clear the context after drag completes
                lock (_dragContextLock)
                {
                    _draggedItem = null;
                }

                Cursor = prevCursor;
            }

            // Reset candidate after drag
            _pressItem = null;
            _pressEventArgs = null;
        }
    }

    private void Items_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressItem = null;
        _pressEventArgs = null;
    }

    // Static helper to retrieve the dragged item (called by drop handlers)
    public static LibraryItemViewModel? GetDraggedItem()
    {
        lock (_dragContextLock)
        {
            return _draggedItem;
        }
    }

    private async void AddHandMoveDefinition_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await OpenNewMoveDefinitionDialog(isFullBody: false);

    private async void AddFullBodyMoveDefinition_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await OpenNewMoveDefinitionDialog(isFullBody: true);

    private async System.Threading.Tasks.Task OpenNewMoveDefinitionDialog(bool isFullBody)
    {
        if (DataContext is not LibraryToolViewModel libVm || libVm.ActiveTimeline is not TimelineEditorViewModel timeline)
            return;

        if (Avalonia.Application.Current is not App app)
            return;

        NewMoveDefinitionViewModel dialogVm = new(isFullBody);
        NewMoveDefinitionResult? result = await app.DialogService.ShowDialogAsync<NewMoveDefinitionResult>(dialogVm);

        if (result == null || string.IsNullOrWhiteSpace(result.Name))
            return;

        timeline.RegisterNewMoveDefinition(result.Name, result.IsFullBody, result.DurationFrames, result.Color);
    }
}
