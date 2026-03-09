// File: .\ViewModels\Timeline\ClipViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public abstract partial class ClipViewModel : ViewModelBase
{
    private string _name;
    private Color _backgroundColor;

    protected ClipViewModel(TimelineClipBase clip, Color backgroundColor, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
    {
        RawClip = clip;
        _backgroundColor = backgroundColor;
        _name = name;
        _rootPath = rootPath;
        _parentTimeline = parentTimeline;
    }

    [Inspectable("Start Beat", "Timing")]
    public double StartBeat
    {
        get => GetStartFrames() / 24d;
        set
        {
            int frames = (int)(value * 24);
            if (GetStartFrames() == frames)
                return;

            double oldStartBeat = GetStartFrames() / 24d;
            SetStartFrames(frames);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(StartBeat)));
            OnStartBeatChanged(oldStartBeat, GetStartFrames() / 24d);
            NotifyClipDataChanged(nameof(StartBeat));
        }
    }

    [Inspectable("Duration", "Timing")]
    public double DurationBeats
    {
        get => GetDurationFrames() / 24d;
        set
        {
            int frames = (int)(value * 24);
            if (GetDurationFrames() == frames)
                return;

            SetDurationFrames(frames);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(DurationBeats)));
            OnDurationBeatsChanged();
            NotifyClipDataChanged(nameof(DurationBeats));
        }
    }

    public virtual string Name
    {
        get => _name;
        set
        {
            if (string.Equals(_name, value, System.StringComparison.Ordinal))
                return;

            _name = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
            NotifyClipDataChanged(nameof(Name));
        }
    }

    [Inspectable("Color", "Appearance")]
    public virtual Color BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor == value)
                return;

            _backgroundColor = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(BackgroundColor)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(RenderColor)));
            OnBackgroundColorChangedCore(value);
        }
    }

    /// <summary>
    /// Color used for rendering. Subclasses may override to source color from a shared definition.
    /// </summary>
    public virtual Color RenderColor => BackgroundColor;

    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public TimelineClipBase RawClip { get; }

    /// <summary>
    /// Whether the clip may be resized via edge dragging (affects cursor and input logic).
    /// Subclasses override when they support resizing.
    /// </summary>
    public virtual bool IsResizable => false;

    protected readonly string? _rootPath;
    protected readonly TimelineEditorViewModel? _parentTimeline;

    protected virtual int GetStartFrames() => RawClip.StartTime;

    protected virtual void SetStartFrames(int frames) => RawClip.StartTime = frames;

    protected virtual void OnStartBeatChanged(double oldStartBeat, double newStartBeat) { }

    protected virtual int GetDurationFrames() => 0;

    protected virtual void SetDurationFrames(int frames) { }

    protected virtual void OnDurationBeatsChanged() { }

    /// <summary>
    /// Virtual hook invoked when BackgroundColor changes. Subclasses may override.
    /// </summary>
    protected virtual void OnBackgroundColorChangedCore(Color value)
    {
        NotifyClipDataChanged(nameof(BackgroundColor));
    }

    protected void NotifyClipDataChanged(string? propertyName) => WeakReferenceMessenger.Default.Send(new Messaging.ClipDataChangedMessage(this, propertyName));

    protected static Color NormalizeOpaque(Color color) => new(255, color.R, color.G, color.B);

    public static string ColorToRgbaHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    }

    public static string ColorToRgbHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static Color ParseRgbaHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Colors.White;

        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length == 6)
        {
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
            {
                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);

                return new Color(255, r, g, b);
            }
        }

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

        if (Color.TryParse($"#{hex}", out Color c))
            return c;
        return Colors.White;
    }
}