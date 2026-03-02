using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class GoldEffectClipViewModel : ClipViewModel
{
    public GoldEffectClipViewModel(GoldEffectClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, name, rootPath, parentTimeline)
    {
    }

    protected override void SyncRawDuration(int frames)
    {
        if (RawClip is GoldEffectClip g)
            g.Duration = frames;
    }
}