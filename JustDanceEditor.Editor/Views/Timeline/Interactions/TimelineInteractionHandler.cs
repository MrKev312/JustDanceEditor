using Avalonia.Input;

namespace JustDanceEditor.Editor.Views.Timeline.Interactions;

/// <summary>
/// Base class for timeline interaction handlers that centralizes pointer capture logic.
/// </summary>
public abstract class TimelineInteractionHandler(TimelineTrackPanel? panel)
{
    protected readonly TimelineTrackPanel? _panel = panel;

    protected void Capture(PointerEventArgs e)
    {
        try
        {
            if (_panel != null)
                e.Pointer.Capture(_panel);
        }
        catch { }
    }

    protected static void Release(PointerEventArgs? e = null)
    {
        try
        {
            e?.Pointer.Capture(null);
        }
        catch { }
    }
}