using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    private void UpdateResizeCursor(Point point, double ppb, double offset)
    {
        // Let the resize handler decide the cursor when available
        if (_resizeHandler != null)
        {
            _resizeHandler.UpdateCursor(point, ppb, (int)offset, Clips);
            return;
        }

        // Fallback: default cursor
        Cursor = new Cursor(StandardCursorType.Arrow);
    }
}