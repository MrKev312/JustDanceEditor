namespace JustDanceEditor.Editor.ViewModels.Timeline;

// Small placeholder to satisfy SnappingService calls when vm is null. It provides default snapping configuration.
public sealed class TimelineEditorViewModelPlaceholder : TimelineEditorViewModel
{
    private static readonly TimelineEditorViewModelPlaceholder _instance = Create();
    public static TimelineEditorViewModelPlaceholder Instance => _instance;

    private TimelineEditorViewModelPlaceholder() : base(null!, string.Empty) { }

    private static TimelineEditorViewModelPlaceholder Create()
    {
        // This constructor is only for placeholder usage; avoid heavy initialization.
        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TimelineEditorViewModelPlaceholder)) as TimelineEditorViewModelPlaceholder;
    }
}
