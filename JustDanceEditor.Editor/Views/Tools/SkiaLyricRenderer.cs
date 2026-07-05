using Avalonia;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;

using SkiaSharp;

using System;
using System.Collections.Generic;
using System.Threading;

namespace JustDanceEditor.Editor.Views.Tools;

internal sealed class SkiaLyricLineLayout : IDisposable
{
    private int _referenceCount = 1;

    private SkiaLyricLineLayout(
        LyricLineViewModel line,
        Rect bounds,
        bool isTextLeftAligned,
        float fontSize,
        List<SkiaLyricSyllable> syllables)
    {
        Line = line;
        Bounds = bounds;
        IsTextLeftAligned = isTextLeftAligned;
        FontSize = fontSize;
        Syllables = syllables;
    }

    public LyricLineViewModel Line { get; }
    public Rect Bounds { get; }
    public bool IsTextLeftAligned { get; }
    public float FontSize { get; }
    public List<SkiaLyricSyllable> Syllables { get; }

    public SkiaLyricLineLayout AddReference()
    {
        while (true)
        {
            int referenceCount = Volatile.Read(ref _referenceCount);
            ObjectDisposedException.ThrowIf(referenceCount <= 0, this);

            if (Interlocked.CompareExchange(ref _referenceCount, referenceCount + 1, referenceCount) == referenceCount)
                return this;
        }
    }

    public static SkiaLyricLineLayout Create(LyricLineViewModel line, Rect bounds, bool isTextLeftAligned)
    {
        double width = bounds.Width;
        double height = bounds.Height;
        if (!HasRenderableBounds(width, height))
            return new SkiaLyricLineLayout(line, bounds, isTextLeftAligned, 0, []);

        float baseFontSize = (float)(height * 0.8);
        using SKFont measureFont = CreateFont(baseFontSize);
        using SKPaint measurePaint = CreateMeasurePaint();

        float totalWidth = 0;
        List<(ClipViewModel Clip, string Text, float BaseWidth)> measured = [];
        foreach (ClipViewModel clip in line.Clips)
        {
            string text = (clip as KaraokeClipViewModel)?.Lyrics ?? string.Empty;
            float syllableWidth = measureFont.MeasureText(text, measurePaint);
            measured.Add((clip, text, syllableWidth));
            totalWidth += syllableWidth;
        }

        double availableWidth = Math.Max(1, width - 40);
        float fontSize = baseFontSize * (float)Math.Min(1.0, availableWidth / Math.Max(1, totalWidth));
        using SKFont textFont = CreateFont(fontSize);
        SKFontMetrics metrics = textFont.Metrics;
        float baseline = (float)bounds.Y + (float)((height - metrics.Descent - metrics.Ascent) / 2.0);

        float scaledWidth = 0;
        List<(ClipViewModel Clip, string Text, float Width)> syllableWidths = [];
        foreach ((ClipViewModel clip, string text, float _) in measured)
        {
            float syllableWidth = textFont.MeasureText(text, measurePaint);
            syllableWidths.Add((clip, text, syllableWidth));
            scaledWidth += syllableWidth;
        }

        float x = isTextLeftAligned
            ? (float)bounds.X
            : (float)(bounds.X + ((width - scaledWidth) / 2.0));

        List<SkiaLyricSyllable> syllables = [];
        foreach ((ClipViewModel clip, string text, float syllableWidth) in syllableWidths)
        {
            SKPath? path = string.IsNullOrEmpty(text)
                ? null
                : textFont.GetTextPath(text, new SKPoint(x, baseline));
            SKRect pathBounds = path?.Bounds ?? new SKRect(x, baseline + metrics.Ascent, x + syllableWidth, baseline + metrics.Descent);
            SKRect hitBounds = new(x, baseline + metrics.Ascent, x + syllableWidth, baseline + metrics.Descent);
            syllables.Add(new SkiaLyricSyllable(clip, path, pathBounds, hitBounds, syllableWidth));
            x += syllableWidth;
        }

        return new SkiaLyricLineLayout(line, bounds, isTextLeftAligned, fontSize, syllables);
    }

    public bool Matches(LyricLineViewModel line, Rect bounds, bool isTextLeftAligned)
        => ReferenceEquals(Line, line)
            && IsTextLeftAligned == isTextLeftAligned
            && Math.Abs(Bounds.X - bounds.X) < 0.1
            && Math.Abs(Bounds.Y - bounds.Y) < 0.1
            && Math.Abs(Bounds.Width - bounds.Width) < 0.1
            && Math.Abs(Bounds.Height - bounds.Height) < 0.1;

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _referenceCount) != 0)
            return;

        foreach (SkiaLyricSyllable syllable in Syllables)
            syllable.Dispose();

        Syllables.Clear();
    }

    private static SKFont CreateFont(float fontSize)
        => new()
        {
            Typeface = SkiaLyricRenderer.Typeface,
            Size = fontSize,
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };

    private static SKPaint CreateMeasurePaint()
        => new()
        {
            IsAntialias = true
        };

    private static bool HasRenderableBounds(double width, double height)
        => width > 0
            && height > 0
            && !double.IsNaN(width)
            && !double.IsNaN(height)
            && !double.IsInfinity(width)
            && !double.IsInfinity(height);
}

internal sealed class SkiaLyricSyllable(ClipViewModel clip, SKPath? path, SKRect pathBounds, SKRect hitBounds, float width) : IDisposable
{
    public ClipViewModel Clip { get; } = clip;
    public SKPath? Path { get; } = path;
    public SKRect PathBounds { get; } = pathBounds;
    public SKRect HitBounds { get; } = hitBounds;
    public float Width { get; } = width;

    public void Dispose() => Path?.Dispose();
}

internal static class SkiaLyricRenderer
{
    internal static readonly SKTypeface Typeface =
        SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;

    public static void Render(SKCanvas canvas, SkiaLyricLineLayout? layout, double currentBeat, Color targetColor, double opacity)
    {
        if (layout == null || layout.Syllables.Count == 0 || opacity <= 0)
            return;

        byte alpha = ToAlpha(opacity);
        SKColor white = new(255, 255, 255, alpha);
        SKColor target = new(targetColor.R, targetColor.G, targetColor.B, ToAlpha(opacity * (targetColor.A / 255.0)));
        SKColor outline = new(0, 0, 0, alpha);

        using SKPaint strokePaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, layout.FontSize * 0.05f),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = outline
        };

        using SKPaint fillPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = white
        };

        foreach (SkiaLyricSyllable syllable in layout.Syllables)
        {
            SKPath? path = syllable.Path;
            if (path == null)
                continue;

            double start = syllable.Clip.StartBeat;
            double end = start + syllable.Clip.DurationBeats;

            canvas.DrawPath(path, strokePaint);

            if (end <= start || currentBeat <= start)
            {
                fillPaint.Shader = null;
                fillPaint.Color = white;
                canvas.DrawPath(path, fillPaint);
            }
            else if (currentBeat >= end)
            {
                fillPaint.Shader = null;
                fillPaint.Color = target;
                canvas.DrawPath(path, fillPaint);
            }
            else
            {
                float progress = (float)Math.Clamp((currentBeat - start) / (end - start), 0, 1);
                fillPaint.Shader = null;
                fillPaint.Color = white;
                canvas.DrawPath(path, fillPaint);

                SKRect clip = syllable.PathBounds;
                clip.Left = syllable.HitBounds.Left - 2;
                clip.Right = syllable.HitBounds.Left + syllable.Width * progress;
                clip.Top -= layout.FontSize;
                clip.Bottom += layout.FontSize;

                canvas.Save();
                canvas.ClipRect(clip, SKClipOperation.Intersect, antialias: false);
                fillPaint.Color = target;
                canvas.DrawPath(path, fillPaint);
                canvas.Restore();
            }
        }
    }

    private static byte ToAlpha(double opacity)
        => (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
}
