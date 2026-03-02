using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class GoldEffectClipViewModel(GoldEffectClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null) : ClipViewModel(clip, duration, color, name, rootPath, parentTimeline)
{
    protected override void SyncRawDuration(int frames)
    {
        if (RawClip is GoldEffectClip g)
            g.Duration = frames;
    }
}