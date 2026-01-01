// File: .\ViewModels\Timeline\ClipViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public abstract partial class ClipViewModel(TimelineClipBase clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null) : ViewModelBase
{
    [Inspectable("Start Beat", "Timing")]
    [ObservableProperty]
    public partial double StartBeat { get; set; } = clip.StartTime / 24d;

    [Inspectable("Duration", "Timing")]
    [ObservableProperty]
    public partial double DurationBeats { get; set; } = duration / 24d;

    [ObservableProperty]
    public partial string Name { get; set; } = name;

    [Inspectable("Color", "Appearance")]
    [ObservableProperty]
    public partial Color BackgroundColor { get; set; } = color;

    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public TimelineClipBase RawClip { get; } = clip;

    protected readonly string? _rootPath = rootPath;
    protected readonly TimelineEditorViewModel? _parentTimeline = parentTimeline;

    partial void OnStartBeatChanged(double value)
    {
        if (RawClip is TimelineClipBase clip)
        {
            clip.StartTime = (int)(value * 24);
            // Broadcast change so other viewers (e.g., lyric preview) can react and re-sort
            WeakReferenceMessenger.Default.Send(new JustDanceEditor.Editor.Messaging.ClipDataChangedMessage(this, nameof(StartBeat)));
        }
    }

    partial void OnDurationBeatsChanged(double value)
    {
        // Subclasses may override to update underlying RawClip duration.
        WeakReferenceMessenger.Default.Send(new JustDanceEditor.Editor.Messaging.ClipDataChangedMessage(this, nameof(DurationBeats)));
    }

    partial void OnNameChanged(string value)
    {
        // Name changes should notify so the Properties pane and other tools can refresh
        NotifyClipDataChanged(nameof(Name));
    }

    partial void OnBackgroundColorChanged(Color value)
    {
        // Delegate to a virtual hook so subclasses can extend behavior safely
        OnBackgroundColorChangedCore(value);
    }

    /// <summary>
    /// Virtual hook invoked when BackgroundColor changes. Subclasses may override.
    /// </summary>
    protected virtual void OnBackgroundColorChangedCore(Color value)
    {
        // Default behavior: broadcast change
        NotifyClipDataChanged(nameof(BackgroundColor));
    }

    protected void NotifyClipDataChanged(string? propertyName) => WeakReferenceMessenger.Default.Send(new JustDanceEditor.Editor.Messaging.ClipDataChangedMessage(this, propertyName));

    public static string ColorToRgbaHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    }

    public static Color ParseRgbaHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Colors.White;
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);
        if (hex.Length == 8)
        {
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgba))
            {
                byte r = (byte)((rgba >> 24) & 0xFF);
                byte g = (byte)((rgba >> 16) & 0xFF);
                byte b = (byte)((rgba >> 8) & 0xFF);
                byte a = (byte)(rgba & 0xFF);

                return new Color(a, r, g, b);
            }
        }
        if (Color.TryParse(hex, out Color c))
            return c;
        return Colors.White;
    }
}