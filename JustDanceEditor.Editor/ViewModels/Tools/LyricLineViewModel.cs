using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public sealed class LyricLineViewModel(List<ClipViewModel> clips)
{
    public List<ClipViewModel> Clips { get; } = clips;
    public double StartBeat => Clips.FirstOrDefault()?.StartBeat ?? 0;
    public double EndBeat => Clips.LastOrDefault() is ClipViewModel last
        ? last.StartBeat + last.DurationBeats
        : 0;
    public string FullText => string.Join("", Clips.Select(static clip => (clip as KaraokeClipViewModel)?.Lyrics ?? ""));
}