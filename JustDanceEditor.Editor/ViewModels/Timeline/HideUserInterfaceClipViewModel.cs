using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class HideUserInterfaceClipViewModel : ClipViewModel
{
    public override bool IsResizable => true;
    public HideUserInterfaceClipViewModel(HideUserInterfaceClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, name, rootPath, parentTimeline)
    {
        // nothing extra to track; clips are always active
    }

    protected override void SyncRawDuration(int frames)
    {
        if (RawClip is HideUserInterfaceClip h)
            h.Duration = frames;
    }

    /// <summary>
    /// Always render using the nominal background color.
    /// </summary>
    public override Color RenderColor => BackgroundColor;
}
