using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

using System;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class GoldEffectClipViewModel : ClipViewModel
{
    public GoldEffectClipViewModel(GoldEffectClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, name, rootPath, parentTimeline)
    {
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is GoldEffectClip g)
            {
                g.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };
    }


}