using Avalonia;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;

using SkiaSharp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
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
        using SKPaint measurePaint = CreateMeasurePaint();

        float totalWidth = 0;
        List<(ClipViewModel Clip, IReadOnlyList<SkiaLyricTextRun> Runs, float BaseWidth)> measured = [];
        foreach (ClipViewModel clip in line.Clips)
        {
            string text = (clip as KaraokeClipViewModel)?.Lyrics ?? string.Empty;
            IReadOnlyList<SkiaLyricTextRun> runs = SkiaLyricRenderer.CreateTextRuns(text);
            float syllableWidth = MeasureText(runs, baseFontSize, measurePaint);
            measured.Add((clip, runs, syllableWidth));
            totalWidth += syllableWidth;
        }

        double availableWidth = Math.Max(1, width - 40);
        float fontSize = baseFontSize * (float)Math.Min(1.0, availableWidth / Math.Max(1, totalWidth));
        (float ascent, float descent) = GetFontMetrics(measured, fontSize);
        float baseline = (float)bounds.Y + (float)((height - descent - ascent) / 2.0);

        float scaledWidth = 0;
        List<(ClipViewModel Clip, IReadOnlyList<SkiaLyricTextRun> Runs, float Width)> syllableWidths = [];
        foreach ((ClipViewModel clip, IReadOnlyList<SkiaLyricTextRun> runs, float _) in measured)
        {
            float syllableWidth = MeasureText(runs, fontSize, measurePaint);
            syllableWidths.Add((clip, runs, syllableWidth));
            scaledWidth += syllableWidth;
        }

        float x = isTextLeftAligned
            ? (float)bounds.X
            : (float)(bounds.X + ((width - scaledWidth) / 2.0));

        List<SkiaLyricSyllable> syllables = [];
        foreach ((ClipViewModel clip, IReadOnlyList<SkiaLyricTextRun> runs, float syllableWidth) in syllableWidths)
        {
            SKPath? path = runs.Count == 0
                ? null
                : CreateTextPath(runs, fontSize, measurePaint, x, baseline);
            SKRect pathBounds = path?.Bounds ?? new SKRect(x, baseline + ascent, x + syllableWidth, baseline + descent);
            SKRect hitBounds = new(x, baseline + ascent, x + syllableWidth, baseline + descent);
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

    private static SKFont CreateFont(SKTypeface typeface, float fontSize)
        => new()
        {
            Typeface = typeface,
            Size = fontSize,
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };

    private static float MeasureText(IReadOnlyList<SkiaLyricTextRun> runs, float fontSize, SKPaint paint)
    {
        float width = 0;
        foreach (SkiaLyricTextRun run in runs)
        {
            using SKFont font = CreateFont(run.Typeface, fontSize);
            width += font.MeasureText(run.Text, paint);
        }

        return width;
    }

    private static (float Ascent, float Descent) GetFontMetrics(
        IReadOnlyList<(ClipViewModel Clip, IReadOnlyList<SkiaLyricTextRun> Runs, float BaseWidth)> measured,
        float fontSize)
    {
        float ascent = 0;
        float descent = 0;
        foreach ((_, IReadOnlyList<SkiaLyricTextRun> runs, _) in measured)
        {
            foreach (SkiaLyricTextRun run in runs)
            {
                using SKFont font = CreateFont(run.Typeface, fontSize);
                SKFontMetrics metrics = font.Metrics;
                ascent = Math.Min(ascent, metrics.Ascent);
                descent = Math.Max(descent, metrics.Descent);
            }
        }

        if (ascent == 0 && descent == 0)
        {
            using SKFont font = CreateFont(SkiaLyricRenderer.PrimaryTypeface, fontSize);
            SKFontMetrics metrics = font.Metrics;
            return (metrics.Ascent, metrics.Descent);
        }

        return (ascent, descent);
    }

    private static SKPath CreateTextPath(
        IReadOnlyList<SkiaLyricTextRun> runs,
        float fontSize,
        SKPaint paint,
        float x,
        float baseline)
    {
        using SKPathBuilder pathBuilder = new();
        foreach (SkiaLyricTextRun run in runs)
        {
            using SKFont font = CreateFont(run.Typeface, fontSize);
            using SKPath? runPath = font.GetTextPath(run.Text, new SKPoint(x, baseline));
            if (runPath != null)
                pathBuilder.AddPath(runPath, SKPathAddMode.Append);
            x += font.MeasureText(run.Text, paint);
        }

        return pathBuilder.Detach();
    }

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

internal readonly record struct SkiaLyricTextRun(string Text, SKTypeface Typeface);

internal static class SkiaLyricRenderer
{
    private static readonly ConcurrentDictionary<int, SKTypeface> TypefacesByCodePoint = new();
    private static readonly string[] FallbackLanguageTags = ["ja", "zh-Hans", "zh-Hant", "ko"];

    internal static readonly SKTypeface PrimaryTypeface =
        SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;

    internal static IReadOnlyList<SkiaLyricTextRun> CreateTextRuns(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<SkiaLyricTextRun> runs = [];
        StringBuilder currentText = new();
        SKTypeface? currentTypeface = null;
        foreach (Rune rune in text.EnumerateRunes())
        {
            SKTypeface typeface = ResolveTypeface(rune);
            if (currentTypeface != null &&
                !string.Equals(currentTypeface.FamilyName, typeface.FamilyName, StringComparison.Ordinal))
            {
                runs.Add(new SkiaLyricTextRun(currentText.ToString(), currentTypeface));
                currentText.Clear();
            }

            currentTypeface = typeface;
            currentText.Append(rune.ToString());
        }

        if (currentTypeface != null)
            runs.Add(new SkiaLyricTextRun(currentText.ToString(), currentTypeface));

        return runs;
    }

    private static SKTypeface ResolveTypeface(Rune rune) =>
        TypefacesByCodePoint.GetOrAdd(rune.Value, static codePoint =>
        {
            using SKFont primaryFont = new(PrimaryTypeface, 16);
            if (primaryFont.ContainsGlyph(codePoint))
                return PrimaryTypeface;

            return SKFontManager.Default.MatchCharacter(
                    PrimaryTypeface.FamilyName,
                    SKFontStyleWeight.Bold,
                    SKFontStyleWidth.Normal,
                    SKFontStyleSlant.Upright,
                    FallbackLanguageTags,
                    codePoint)
                ?? PrimaryTypeface;
        });

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
                clip.Right = syllable.HitBounds.Left + (syllable.Width * progress);
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
