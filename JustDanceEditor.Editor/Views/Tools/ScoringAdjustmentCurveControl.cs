using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Tools;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class ScoringAdjustmentCurveControl : ThemeAwareGraphControl
{
    internal const double MaxDistance = ScoringAdjustmentPreviewAnalyzer.CurveMaxDistance;

    public static readonly StyledProperty<ScoringAdjustmentCurve?> CurveProperty =
        AvaloniaProperty.Register<ScoringAdjustmentCurveControl, ScoringAdjustmentCurve?>(nameof(Curve));

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<ScoringAdjustmentCurveControl, string>(nameof(EmptyText), "No scoring curve yet");

    private static readonly SolidColorBrush SampleOutlineBrush = new(Color.FromArgb(165, 12, 16, 24));
    private static readonly Pen SampleOutlinePen = new(SampleOutlineBrush, 1);
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    static ScoringAdjustmentCurveControl()
    {
        AffectsRender<ScoringAdjustmentCurveControl>(
            CurveProperty,
            EmptyTextProperty);
    }

    public ScoringAdjustmentCurve? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public string EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds;
        if (bounds.Width < 140 || bounds.Height < 120)
            return;

        ScoringAdjustmentCurve? curve = Curve;
        Rect plot = GetPlotBounds(bounds);
        DrawGridAndAxes(context, plot, MaxDistance);

        if (curve == null || (curve.Points.Count == 0 && curve.Samples.Count == 0))
        {
            DrawText(context, EmptyText, 12, TextBrush, new Point(plot.Left + 12, plot.Top + 12));
            return;
        }

        DrawCurve(context, plot, curve.Points, MaxDistance);
        DrawCurrentSamples(context, plot, curve.Samples, MaxDistance);
    }

    private void DrawGridAndAxes(DrawingContext context, Rect plot, double maxDistance)
    {
        for (int i = 0; i <= 4; i++)
        {
            double ratio = i / 4.0;
            double value = 100.0 * ratio;

            double y = plot.Bottom - (plot.Height * ratio);
            context.DrawLine(new Pen(GridBrush, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(context, value.ToString("0", CultureInfo.InvariantCulture), 10, TextBrush, new Point(6, y - 7));
        }

        for (int i = 0; i <= 4; i++)
        {
            double ratio = i / 4.0;
            double value = maxDistance * ratio;
            double x = plot.Left + (plot.Width * ratio);
            context.DrawLine(new Pen(GridBrush, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
            double labelOffset = i == 0 ? 0 : i == 4 ? 20 : 10;
            DrawText(context, FormatDistanceLabel(value), 10, MutedTextBrush, new Point(x - labelOffset, plot.Bottom + 5));
        }

        Pen axisPen = new(AxisBrush, 1);
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        DrawText(context, "distance", 10, MutedTextBrush, new Point(plot.Right - 42, plot.Bottom + 20));
        DrawText(context, "score %", 10, MutedTextBrush, new Point(plot.Left + 8, plot.Top + 4));
    }

    private void DrawCurve(DrawingContext context, Rect plot, IReadOnlyList<ScoringAdjustmentCurvePoint> points, double maxDistance)
    {
        if (points.Count == 0)
            return;

        List<Point> screenPoints = [.. points.Select(point => ToScreenPoint(plot, point.StatisticalDistance, point.PercentageScore, maxDistance))];
        DrawPolyline(context, screenPoints, new Pen(TextBrush, 2.6));
    }

    private void DrawCurrentSamples(DrawingContext context, Rect plot, IReadOnlyList<ScoringAdjustmentCurveSample> samples, double maxDistance)
    {
        foreach (ScoringAdjustmentCurveSample sample in samples)
        {
            Point current = ToScreenPoint(plot, sample.StatisticalDistance, sample.PercentageScore, maxDistance);
            SolidColorBrush fill = new(GetSeriesColor(sample.SeriesIndex, 220));
            context.DrawEllipse(fill, SampleOutlinePen, current, 3.8, 3.8);
        }
    }

    private static void DrawPolyline(DrawingContext context, IReadOnlyList<Point> points, Pen pen)
    {
        if (points.Count == 0)
            return;

        if (points.Count == 1)
        {
            context.DrawEllipse(pen.Brush, null, points[0], 2.5, 2.5);
            return;
        }

        StreamGeometry geometry = new();
        using StreamGeometryContext g = geometry.Open();
        g.BeginFigure(points[0], isFilled: false);
        for (int i = 1; i < points.Count; i++)
            g.LineTo(points[i]);
        context.DrawGeometry(null, pen, geometry);
    }

    private static Rect GetPlotBounds(Rect bounds)
        => new(48, 12, Math.Max(1, bounds.Width - 62), Math.Max(1, bounds.Height - 44));

    private static Point ToScreenPoint(Rect plot, float statisticalDistance, float percentageScore, double maxDistance)
        => new(
            plot.Left + (NormalizeDistance(statisticalDistance, maxDistance) / maxDistance * plot.Width),
            plot.Bottom - (NormalizeScore(percentageScore) / 100.0 * plot.Height));

    private static double NormalizeDistance(float distance, double maxDistance)
    {
        if (float.IsNaN(distance) || float.IsInfinity(distance))
            return 0.0;

        return Math.Clamp(distance, 0.0f, (float)maxDistance);
    }

    private static double NormalizeScore(float score)
    {
        if (float.IsNaN(score) || float.IsInfinity(score))
            return 0.0;

        return Math.Clamp(score, 0.0f, 100.0f);
    }

    private static string FormatDistanceLabel(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point point)
    {
        FormattedText formatted = new(text, Culture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, point);
    }
}
