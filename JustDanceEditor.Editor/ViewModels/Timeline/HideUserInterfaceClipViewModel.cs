using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Editor.Attributes;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class HideUserInterfaceClipViewModel : ClipViewModel
{
    public override bool IsResizable => true;
    public HideUserInterfaceClipViewModel(HideUserInterfaceClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, name, rootPath, parentTimeline)
    {
        // nothing extra to track; clips are always active
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is HideUserInterfaceClip h2)
            {
                h2.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };
    }

    /// <summary>
    /// Always render using the nominal background color.
    /// </summary>
    public override Color RenderColor => BackgroundColor;
}
