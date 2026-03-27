using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

// Small placeholder to satisfy SnappingService calls when vm is null. It provides default snapping configuration.
public sealed class TimelineEditorViewModelPlaceholder : TimelineEditorViewModel
{
    public static TimelineEditorViewModelPlaceholder Instance { get; } = new();

    private TimelineEditorViewModelPlaceholder()
        : base(new IntermediateSongPackage(), string.Empty, new PlaybackService(), new TimelineSettingsService())
    {
    }
}