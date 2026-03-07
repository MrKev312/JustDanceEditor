using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Formats.JDI.Timelines;

using System.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class KaraokeClipViewModel : ClipViewModel, IHasSharedColorSource
{
    private KaraokeClip KaraokeClip => (KaraokeClip)RawClip;

    public override bool IsResizable => true;

    [Inspectable("Lyrics", "Karaoke")]
    public string Lyrics
    {
        get => KaraokeClip.Lyrics ?? string.Empty;
        set
        {
            value ??= string.Empty;
            if (string.Equals(KaraokeClip.Lyrics, value, System.StringComparison.Ordinal))
                return;

            KaraokeClip.Lyrics = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Lyrics)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
            NotifyClipDataChanged(nameof(Lyrics));
            NotifyClipDataChanged(nameof(Name));
        }
    }

    [Inspectable("End of Line", "Karaoke")]
    public bool IsEndOfLine
    {
        get => KaraokeClip.IsEndOfLine;
        set
        {
            if (KaraokeClip.IsEndOfLine == value)
                return;

            KaraokeClip.IsEndOfLine = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsEndOfLine)));
            NotifyClipDataChanged(nameof(IsEndOfLine));
        }
    }

    public override string Name
    {
        get => Lyrics;
        set => Lyrics = value;
    }

    public KaraokeClipViewModel(KaraokeClip clip, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, parentTimeline?.LyricsDefinitionColor ?? Colors.Yellow, clip.Lyrics ?? string.Empty, rootPath, parentTimeline)
    {
        // If we have a parent timeline, subscribe to timeline PropertyChanged so
        // we can refresh rendering when the lyrics definition color changes.
        if (_parentTimeline != null)
        {
            try
            {
                _parentTimeline.PropertyChanged += OnParentTimelinePropertyChanged;
            }
            catch { }
        }
    }

    protected override int GetDurationFrames() => KaraokeClip.Duration;

    protected override void SetDurationFrames(int frames) => KaraokeClip.Duration = frames;

    public override Color RenderColor => _parentTimeline != null ? NormalizeOpaque(_parentTimeline.LyricsDefinitionColor) : base.RenderColor;

    // Shadow BackgroundColor so Properties panel reads/writes the LyricsDefinition color
    [Inspectable("Color", "Appearance")]
    public new Color BackgroundColor
    {
        get => _parentTimeline != null ? NormalizeOpaque(_parentTimeline.LyricsDefinitionColor) : base.BackgroundColor;
        set
        {
            if (_parentTimeline != null)
            {
                Color normalized = NormalizeOpaque(value);
                if (_parentTimeline.LyricsDefinitionColor == normalized)
                    return;

                _parentTimeline.LyricsDefinitionColor = normalized;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(BackgroundColor)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(RenderColor)));
                NotifyClipDataChanged(nameof(BackgroundColor));
            }
            else
            {
                base.BackgroundColor = value;
            }
        }
    }

    private void OnParentTimelinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(TimelineEditorViewModel.LyricsDefinitionColor)) or (nameof(TimelineEditorViewModel.LyricsColor)))
        {
            // When the timeline-level lyrics color changes, update rendering
            NotifyClipDataChanged(nameof(BackgroundColor));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(BackgroundColor)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(RenderColor)));
        }
    }

    // IHasSharedColorSource
    public (object Target, string PropertyName)? GetColorEditTarget(string inspectedProperty, TimelineEditorViewModel timeline)
    {
        if (inspectedProperty != nameof(BackgroundColor))
            return null;
        return (timeline, nameof(TimelineEditorViewModel.LyricsDefinitionColor));
    }
}