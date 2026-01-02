using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Tools;

using System;
using System.Threading;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class LibraryToolView : UserControl
{
    private Point _pressPoint;
    private LibraryItemViewModel? _pressItem;
    private const double DragThreshold = 4.0;

    // Static context for passing library items during drag-drop (avoids obsolete DataObject)
    private static LibraryItemViewModel? _draggedItem;
    private static readonly Lock _dragContextLock = new();

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

        _pressPoint = e.GetCurrentPoint(this).Position;

        // Try to determine the exact item under pointer (DataGridRow) to be more robust than SelectedItem
        LibraryItemViewModel? hit = default;
        if (e.Source is Visual v)
        {
            Visual? cur = v;
            while (cur is not null and not DataGridRow)
                cur = cur.GetVisualParent() as Visual;
            if (cur is DataGridRow row)
            {
                hit = row.DataContext as LibraryItemViewModel;
            }
        }

        // Fallback to selected item only if we haven't found a row-level item
        if (hit == null && sender is DataGrid dg && dg.SelectedItem is LibraryItemViewModel selectedItem)
        {
            hit = selectedItem;
        }

        _pressItem = hit;
        // store candidate for drag initiation
        if (_pressItem != null)
        {
            // nothing else required here
        }
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

            // Use minimal data object to initiate drag (the item reference is in static context)
#pragma warning disable CS0618
            DataObject data = new();
            data.Set(DataFormats.Text, "LibraryItem");
            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
#pragma warning restore CS0618
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
        }
    }

    private void Items_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressItem = null;
    }

    // Static helper to retrieve the dragged item (called by drop handlers)
    public static LibraryItemViewModel? GetDraggedItem()
    {
        lock (_dragContextLock)
        {
            return _draggedItem;
        }
    }
}