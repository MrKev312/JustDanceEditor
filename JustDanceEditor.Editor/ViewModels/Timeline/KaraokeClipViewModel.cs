using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using JustDanceEditor.Editor.Messaging;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Editor.Attributes;
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

        // Listen for background color changes to update metadata and sync across lyrics clips
        this.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BackgroundColor) && _parentTimeline != null)
            {
                var lyricsClips = _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<KaraokeClipViewModel>().ToList();
                foreach (var c in lyricsClips)
                {
                    if (!Equals(c.BackgroundColor, BackgroundColor))
                        c.BackgroundColor = BackgroundColor;
                }

                _parentTimeline.UpdateLyricsColor(ColorToRgbaHex(BackgroundColor));
                NotifyClipDataChanged(nameof(BackgroundColor));
            }
        };
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
}
