// File: .\ViewModels\Timeline\ClipViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public abstract partial class ClipViewModel(TimelineClipBase clip, Color backgroundColor, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null) : ViewModelBase, IDisposable
{
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
        get => name;
        set
        {
            if (string.Equals(name, value, System.StringComparison.Ordinal))
                return;

            name = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
            NotifyClipDataChanged(nameof(Name));
        }
    }

    public virtual Color BackgroundColor
    {
        get => backgroundColor;
        set
        {
            if (backgroundColor == value)
                return;

            backgroundColor = value;
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

    public TimelineClipBase RawClip { get; } = clip;

    /// <summary>
    /// Whether the clip may be resized via edge dragging (affects cursor and input logic).
    /// Subclasses override when they support resizing.
    /// </summary>
    public virtual bool IsResizable => false;

    protected readonly string? _rootPath = rootPath;
    protected readonly TimelineEditorViewModel? _parentTimeline = parentTimeline;

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

    /// <summary>
    /// When true, NotifyClipDataChanged is a no-op. Set this during batch moves to suppress
    /// per-clip messenger broadcasts; call FlushDataChangedNotification afterward.
    /// </summary>
    internal bool SuppressDataChangeMessages { get; set; }

    protected void NotifyClipDataChanged(string? propertyName)
    {
        if (SuppressDataChangeMessages)
            return;
        WeakReferenceMessenger.Default.Send(new Messaging.ClipDataChangedMessage(this, propertyName));
    }

    internal void FlushDataChangedNotification(string? propertyName)
        => WeakReferenceMessenger.Default.Send(new Messaging.ClipDataChangedMessage(this, propertyName));

    public virtual void Dispose()
        => GC.SuppressFinalize(this);

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