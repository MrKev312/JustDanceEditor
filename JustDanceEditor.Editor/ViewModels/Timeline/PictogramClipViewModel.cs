using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System.IO;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class PictogramClipViewModel : ClipViewModel
{
    [Inspectable("Pictogram Id", "Pictogram")]
    [ObservableProperty]
    public partial string PictogramId { get; set; } = string.Empty;

    public PictogramClipViewModel(PictogramClip clip, double duration, Color color, string pictogramId, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, pictogramId, rootPath, parentTimeline)
    {
        PictogramId = clip.PictogramId ?? string.Empty;
        if (!string.IsNullOrEmpty(PictogramId) && rootPath != null)
            ImagePath = Path.Combine(rootPath, "assets", "pictograms", $"{PictogramId}.webp");

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is PictogramClip p)
            {
                p.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };
    }

    partial void OnPictogramIdChanged(string value)
    {
        if (RawClip is PictogramClip p)
        {
            p.PictogramId = value;
            if (!string.IsNullOrEmpty(value) && _rootPath != null)
                ImagePath = Path.Combine(_rootPath, "assets", "pictograms", $"{value}.webp");

            NotifyClipDataChanged(nameof(PictogramId));
        }
    }
}