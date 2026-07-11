using Avalonia;
using Avalonia.Input;

namespace JustDanceEditor.Editor.Views.Timeline;

internal static class TimelineResizeCursor
{
    public static void Update(TimelineTrackPanel owner, Point point, double pixelsPerBeat, double offset)
    {
        if (owner.ResizeHandler != null)
        {
            owner.ResizeHandler.UpdateCursor(point, pixelsPerBeat, (int)offset, owner.Clips);
            return;
        }

        owner.Cursor = new Cursor(StandardCursorType.Arrow);
    }
}
