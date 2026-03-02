using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class HideUserInterfaceClipViewModel(HideUserInterfaceClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null) : ClipViewModel(clip, duration, color, name, rootPath, parentTimeline)
{
    public override bool IsResizable => true;

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