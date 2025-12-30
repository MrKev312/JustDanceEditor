using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI.Timelines;
using System;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Tools;

public class LyricLineControl : Control
{
    public static readonly StyledProperty<LyricLineViewModel?> LineProperty =
        AvaloniaProperty.Register<LyricLineControl, LyricLineViewModel?>(nameof(Line));

    public LyricLineViewModel? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public static readonly StyledProperty<double> CurrentBeatProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(CurrentBeat));

    public double CurrentBeat
    {
        get => GetValue(CurrentBeatProperty);
        set => SetValue(CurrentBeatProperty, value);
    }

    public static readonly StyledProperty<Color> TargetColorProperty =
        AvaloniaProperty.Register<LyricLineControl, Color>(nameof(TargetColor), Colors.SkyBlue);

    public Color TargetColor
    {
        get => GetValue(TargetColorProperty);
        set => SetValue(TargetColorProperty, value);
    }

    static LyricLineControl()
    {
        AffectsRender<LyricLineControl>(LineProperty, CurrentBeatProperty, TargetColorProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (Line == null || Line.Clips.Count == 0)
            return;

        var typeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
        double baseFontSize = Bounds.Height * 0.8;
        
        // 1. First pass: Measure everything at base font size
        var measureSyllables = Line.Clips.Select(c =>
        {
            string text = (c.RawClip as KaraokeClip)?.Lyrics ?? "";

            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                baseFontSize,
                Brushes.White
            );
            return new { Clip = c, Text = text, FT = ft };
        }).ToList();

        double totalWidth = measureSyllables.Sum(s => s.FT.WidthIncludingTrailingWhitespace);
        
        // 2. Calculate scaling to fit within bounds (with 20px margin on each side)
        double scale = Math.Min(1.0, (Bounds.Width - 40) / Math.Max(1, totalWidth));
        double fontSize = baseFontSize * scale;
        
        // 3. Second pass: Create final layouts at scaled size
        var syllables = measureSyllables.Select(s => {
            var ft = new FormattedText(
                s.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White
            );
            return new { s.Clip, s.Text, FT = ft };
        }).ToList();

        double scaledWidth = syllables.Sum(s => s.FT.WidthIncludingTrailingWhitespace);
        double x = (Bounds.Width - scaledWidth) / 2.0;
        double y = (Bounds.Height - fontSize) / 2.0;

        double beat = CurrentBeat;

        foreach (var s in syllables)
        {
            double start = s.Clip.StartBeat;
            double end = start + s.Clip.DurationBeats;
            
            IBrush fillBrush;

            if (beat >= end)
            {
                fillBrush = new SolidColorBrush(TargetColor);
            }
            else if (beat <= start)
            {
                fillBrush = Brushes.White;
            }
            else
            {
                double progress = (beat - start) / (end - start);
                fillBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(TargetColor, progress),
                        new GradientStop(Colors.White, progress)
                    }
                };
            }

            // Create final FormattedText and generate geometry for outline rendering
            var finalFt = new FormattedText(
                s.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fillBrush
            );

            var textGeometry = finalFt.BuildGeometry(new Point(x, y));
            if (textGeometry != null)
            {
                // Draw outline first
                context.DrawGeometry(null, new Pen(Brushes.Black, Math.Max(1.0, fontSize * 0.05), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), textGeometry);
                // Then draw the fill
                context.DrawGeometry(fillBrush, null, textGeometry);
            }

            x += finalFt.WidthIncludingTrailingWhitespace;
        }
    }
}
