// File: .\ViewModels\Timeline\ClipViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System.IO;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class ClipViewModel : ViewModelBase
{
    [Inspectable("Start Beat", "Timing")]
    [ObservableProperty]
    public partial double StartBeat { get; set; }

    [Inspectable("Duration", "Timing")]
    [ObservableProperty]
    public partial double DurationBeats { get; set; }

    [Inspectable("Name", "General")]
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [Inspectable("Color", "Appearance")]
    [ObservableProperty]
    public partial Color BackgroundColor { get; set; }

    [Inspectable("Type", "Info", isReadOnly: true)]
    public string ClipType => RawClip?.GetType().Name ?? "Unknown";

    [ObservableProperty]
    public partial object RawClip { get; set; }

    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnStartBeatChanged(double value)
    {
        if (RawClip is TimelineClipBase clip)
        {
            clip.StartTime = (int)(value * 24);
        }
    }

    partial void OnDurationBeatsChanged(double value)
    {
        int duration = (int)(value * 24);
        if (RawClip is KaraokeClip k)
            k.Duration = duration;
        else if (RawClip is PictogramClip p)
            p.Duration = duration;
        else if (RawClip is GoldEffectClip g)
            g.Duration = duration;
    }

    partial void OnNameChanged(string value)
    {
        if (RawClip is KaraokeClip karaoke)
        {
            karaoke.Lyrics = value;
        }
        else if (RawClip is PictogramClip picto)
        {
            picto.PictogramId = value;
            if (!string.IsNullOrEmpty(value) && _rootPath != null)
            {
                 ImagePath = Path.Combine(_rootPath, "assets", "pictograms", $"{value}.webp");
            }
        }
        else if (RawClip is MoveClip move)
        {
            move.MoveId = value;
        }
    }
    
    private readonly string? _rootPath;

    public ClipViewModel(TimelineClipBase clip, double duration, Color color, string name, string? rootPath = null)
    {
        _rootPath = rootPath;
        RawClip = clip;
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