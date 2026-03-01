using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class KaraokeClipViewModel : ClipViewModel
{
    public override bool IsResizable => true;
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

        // Keep duration in sync with underlying model when edited
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is KaraokeClip k)
            {
                k.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };

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

    public override Color RenderColor => _parentTimeline != null ? new Color(255, _parentTimeline.LyricsDefinitionColor.R, _parentTimeline.LyricsDefinitionColor.G, _parentTimeline.LyricsDefinitionColor.B) : base.RenderColor;

    // Shadow BackgroundColor so Properties panel reads/writes the LyricsDefinition color
    [Inspectable("Color", "Appearance")]
    public new Color BackgroundColor
    {
        get => _parentTimeline != null ? new Color(255, _parentTimeline.LyricsDefinitionColor.R, _parentTimeline.LyricsDefinitionColor.G, _parentTimeline.LyricsDefinitionColor.B) : base.BackgroundColor;
        set
        {
            if (_parentTimeline != null)
            {
                Color normalized = new(255, value.R, value.G, value.B);
                _parentTimeline.LyricsDefinitionColor = normalized;
            }
            else
            {
                base.BackgroundColor = value;
            }
        }
    }

    protected override void OnBackgroundColorChangedCore(Color value)
    {
        // Do not update timeline metadata from individual clip changes anymore.
        // Timeline-level lyrics color is the single source-of-truth and will be
        // set via the LyricsDefinition color by the Properties editor.

        // Always invoke base to notify listeners
        base.OnBackgroundColorChangedCore(value);
    }

    private void OnParentTimelinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(TimelineEditorViewModel.LyricsDefinitionColor)) or (nameof(TimelineEditorViewModel.LyricsColor)))
        {
            // When the timeline-level lyrics color changes, update rendering
            NotifyClipDataChanged(nameof(BackgroundColor));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(RenderColor)));
        }
    }
}