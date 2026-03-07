using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class GoldEffectClipViewModel(GoldEffectClip clip, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null) : ClipViewModel(clip, Colors.Gold, "Gold Effect", rootPath, parentTimeline)
{
    private GoldEffectClip GoldEffectClip => (GoldEffectClip)RawClip;

    protected override int GetDurationFrames() => GoldEffectClip.Duration;

    protected override void SetDurationFrames(int frames) => GoldEffectClip.Duration = frames;
}