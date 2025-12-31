// File: .\ViewModels\Timeline\ClipViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Formats.JDI.Timelines;

using System.IO;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class ClipViewModel : ViewModelBase
{
    [ObservableProperty] private double _startBeat;
    [ObservableProperty] private double _durationBeats;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Color _backgroundColor;
    [ObservableProperty] private object _rawClip;
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private bool _isSelected;

    public ClipViewModel(TimelineClipBase clip, double duration, Color color, string name, string? rootPath = null)
    {
        _rawClip = clip;
        // StartTime is in 24ths of a beat relative to the first beat marker
        StartBeat = clip.StartTime / 24d;
        DurationBeats = duration / 24d;
        Name = name;
        BackgroundColor = color;

        if (clip is PictogramClip picto && rootPath != null)
        {
            ImagePath = Path.Combine(rootPath, "assets", "pictograms", $"{picto.PictogramId}.webp");
        }
    }
}