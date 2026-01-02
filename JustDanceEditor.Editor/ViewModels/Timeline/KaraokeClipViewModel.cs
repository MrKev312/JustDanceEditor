using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Messaging;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class KaraokeClipViewModel : ClipViewModel
{
    [Inspectable("Lyrics", "Karaoke")]
    [ObservableProperty]
    public partial string Lyrics { get; set; } = string.Empty;

    [Inspectable("End of Line", "Karaoke")]
    [ObservableProperty]
    public partial bool IsEndOfLine { get; set; }

    public KaraokeClipViewModel(KaraokeClip clip, double duration, Color color, string lyrics, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, lyrics, rootPath, parentTimeline)
    {
        Lyrics = clip.Lyrics ?? string.Empty;
        IsEndOfLine = clip.IsEndOfLine;
        // keep Name in sync for display
        Name = Lyrics;

        // No-op: background color changes handled by overriding OnBackgroundColorChangedCore

    }

    partial void OnLyricsChanged(string value)
    {
        if (RawClip is KaraokeClip k)
        {
            k.Lyrics = value;
            Name = value;
            // notify listeners
            NotifyClipDataChanged(nameof(Lyrics));
        }
    }

    partial void OnIsEndOfLineChanged(bool value)
    {
        if (RawClip is KaraokeClip k)
        {
            k.IsEndOfLine = value;
            NotifyClipDataChanged(nameof(IsEndOfLine));
        }
    }

    protected override void OnBackgroundColorChangedCore(Color value)
    {
        // When one lyrics clip changes color, update all lyrics clips and metadata
        if (_parentTimeline != null)
        {
            List<KaraokeClipViewModel> lyricsClips = _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<KaraokeClipViewModel>().ToList();
            foreach (var clip in lyricsClips)
            {
                if (!Equals(clip.BackgroundColor, value))
                    clip.BackgroundColor = value;
            }

            _parentTimeline.UpdateLyricsColor(ColorToRgbaHex(value));
        }

        // Always invoke base to notify listeners
        base.OnBackgroundColorChangedCore(value);
    }
}