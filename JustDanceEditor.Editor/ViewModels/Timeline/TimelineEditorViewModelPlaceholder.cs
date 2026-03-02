using System.Runtime.CompilerServices;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

// Small placeholder to satisfy SnappingService calls when vm is null. It provides default snapping configuration.
public sealed class TimelineEditorViewModelPlaceholder : TimelineEditorViewModel
{
    public static TimelineEditorViewModelPlaceholder Instance { get; } = Create();

    private TimelineEditorViewModelPlaceholder() : base(null!, string.Empty, null!, null!) { }

    private static TimelineEditorViewModelPlaceholder Create()
    {
        // This constructor is only for placeholder usage; avoid heavy initialization.
        return (RuntimeHelpers.GetUninitializedObject(typeof(TimelineEditorViewModelPlaceholder)) as TimelineEditorViewModelPlaceholder)!;
    }
}