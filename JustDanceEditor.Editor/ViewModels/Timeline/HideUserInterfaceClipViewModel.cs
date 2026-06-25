using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class HideUserInterfaceClipViewModel(HideUserInterfaceClip clip, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null) : ClipViewModel(clip, Colors.MediumPurple, string.Empty, rootPath, parentTimeline)
{
    public override bool IsResizable => true;

    private HideUserInterfaceClip HideUserInterfaceClip => (HideUserInterfaceClip)RawClip;

    protected override int GetDurationFrames() => HideUserInterfaceClip.Duration;

    protected override void SetDurationFrames(int frames) => HideUserInterfaceClip.Duration = frames;

    /// <summary>
    /// Always render using the nominal background color.
    /// </summary>
    public override Color RenderColor => BackgroundColor;
}