// File: .\ViewModels\Timeline\ClipViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.IO;
using System.Linq;

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

    private readonly string? _rootPath;
    private TimelineEditorViewModel? _parentTimeline;

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

    partial void OnBackgroundColorChanged(Color value)
    {
        // When lyrics color changes, update all lyrics clips and notify the timeline for rerender
        if (RawClip is KaraokeClip && _parentTimeline != null)
        {
            // Update all lyrics clips to have the same background color
            var lyricsTrack = _parentTimeline.Tracks.FirstOrDefault(t => t.Title == "Lyrics");
            if (lyricsTrack != null)
            {
                foreach (var clip in lyricsTrack.Clips)
                {
                    if (clip.RawClip is KaraokeClip)
                    {
                        clip.BackgroundColor = value;
                    }
                }
            }

            // Update the metadata with the new color in RGBA format
            _parentTimeline.UpdateLyricsColor(ColorToRgbaHex(value));
        }
    }

    /// <summary>
    /// Converts a Color to RGBA hex format (e.g., #RRGGBBAA)
    /// </summary>
    public static string ColorToRgbaHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    }

    /// <summary>
    /// Parses a color from RGBA hex format and converts to Avalonia Color (ARGB).
    /// Handles both #RRGGBBAA (RGBA) and #AARRGGBB (ARGB) formats.
    /// </summary>
    public static Color ParseRgbaHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Colors.White;

        // Remove '#' if present
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        // Handle 8-character hex codes
        if (hex.Length == 8)
        {
            // Try to parse as RGBA first (most common in our case)
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgba))
            {
                byte r = (byte)((rgba >> 24) & 0xFF);
                byte g = (byte)((rgba >> 16) & 0xFF);
                byte b = (byte)((rgba >> 8) & 0xFF);
                byte a = (byte)(rgba & 0xFF);

                return new Color(a, r, g, b);
            }
        }

        // Fall back to standard Avalonia parsing for other formats
        if (Color.TryParse(hex, out Color c))
            return c;

        return Colors.White;
    }

    public ClipViewModel(TimelineClipBase clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
    {
        _rootPath = rootPath;
        _parentTimeline = parentTimeline;
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