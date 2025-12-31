using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimeRulerControl : Control
{
    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<TimeRulerControl, double>(nameof(PixelsPerBeat), 50.0);

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public static readonly StyledProperty<int> BeatOffsetProperty =
        AvaloniaProperty.Register<TimeRulerControl, int>(nameof(BeatOffset), 0);

    public int BeatOffset
    {
        get => GetValue(BeatOffsetProperty);
        set => SetValue(BeatOffsetProperty, value);
    }

    public static readonly StyledProperty<double> MaxBeatProperty =
        AvaloniaProperty.Register<TimeRulerControl, double>(nameof(MaxBeat), 0.0);

    public double MaxBeat
    {
        get => GetValue(MaxBeatProperty);
        set => SetValue(MaxBeatProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<SignatureSegment>> SignaturesProperty =
        AvaloniaProperty.Register<TimeRulerControl, IEnumerable<SignatureSegment>>(nameof(Signatures));

    public IEnumerable<SignatureSegment> Signatures
    {
        get => GetValue(SignaturesProperty);
        set => SetValue(SignaturesProperty, value);
    }

    static TimeRulerControl()
    {
        AffectsRender<TimeRulerControl>(PixelsPerBeatProperty, BeatOffsetProperty, MaxBeatProperty, SignaturesProperty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);
            SeekToPoint(point.Position.X);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
        {
            PointerPoint point = e.GetCurrentPoint(this);
            SeekToPoint(point.Position.X);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void SeekToPoint(double x)
    {
        double beatIndex = x / PixelsPerBeat;
        double beatLabel = Math.Round(beatIndex + BeatOffset);

        if (DataContext is TimelineEditorViewModel vm)
        {
            vm.Playback.SeekToBeat(beatLabel);
        }
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;
        double max = MaxBeat;

        // 0. Catch all pointer events by drawing a transparent background
        context.FillRectangle(Brushes.Transparent, bounds);

        // 1. Draw Alternating Measure Backgrounds
        if (Signatures != null)
        {
            var sortedSig = Signatures.OrderBy(s => s.Marker).ToList();
            if (sortedSig.Count == 0)
            {
                // Fallback to 4/4 if no signatures
                DrawMeasures(context, 0, max, 4, ppb, offset, bounds.Height);
            }
            else
            {
                for (int i = 0; i < sortedSig.Count; i++)
                {
                    double startBeat = sortedSig[i].Marker - offset;
                    double endBeat = (i + 1 < sortedSig.Count) ? sortedSig[i + 1].Marker - offset : max;
                    int beatsPerMeasure = sortedSig[i].Beats;

                    DrawMeasures(context, startBeat, endBeat, beatsPerMeasure, ppb, offset, bounds.Height, i % 2 != 0);
                }
            }
        }

        // 2. Draw Ticks and Labels
        var mainPen = new Pen(Brushes.DimGray, 1);
        var tickPen = new Pen(Brushes.Gray, 1);
        IImmutableSolidColorBrush labelBrush = Brushes.LightGray;

        context.DrawLine(mainPen, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));

        for (int i = 0; i <= max; i++)
        {
            double x = i * ppb;
            if (x < 0)
                continue;
            if (x > bounds.Width)
                break;

            bool isMajor = (i + offset) % 4 == 0; // Keeping 4 for labels for now, or could sync with signatures too
            double tickHeight = isMajor ? 12 : 6;
            
            context.DrawLine(tickPen, new Point(x, bounds.Height), new Point(x, bounds.Height - tickHeight));

            if (isMajor)
            {
                var text = new FormattedText(
                    (i + offset).ToString(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    10,
                    labelBrush);
                
                context.DrawText(text, new Point(x + 3, bounds.Height - tickHeight - 12));
            }
        }
    }

    private void DrawMeasures(DrawingContext context, double startBeat, double endBeat, int bpm, double ppb, int offset, double height, bool startAlt = false)
    {
        // Internal measure calculation relative to the marker start
        // We need to know which measure index we are at to alternate colors
        double actualStartBeat = startBeat + offset;
        double actualEndBeat = endBeat + offset;

        var brushA = new SolidColorBrush(Colors.White, 0.05);
        var brushB = new SolidColorBrush(Colors.White, 0.02);

        int measureIndex = 0;
        for (double b = actualStartBeat; b < actualEndBeat; b += bpm)
        {
            double mStart = b;
            double mEnd = Math.Min(actualEndBeat, b + bpm);

            double xStart = (mStart - offset) * ppb;
            double xEnd = (mEnd - offset) * ppb;

            if (xEnd < 0)
            {
                measureIndex++;
                continue;
            }

            if (xStart > Bounds.Width)
                break;

            SolidColorBrush brush = (measureIndex % 2 == 0) ? brushA : brushB;
            context.FillRectangle(brush, new Rect(xStart, 0, xEnd - xStart, height));
            
            measureIndex++;
        }
    }
}
