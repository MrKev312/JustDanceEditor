using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Tools;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class ScoringAdjustmentGraphControl : ThemeAwareGraphControl
{
    public static readonly StyledProperty<IReadOnlyList<ScoringAdjustmentSweepSeries>?> SeriesProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, IReadOnlyList<ScoringAdjustmentSweepSeries>?>(nameof(Series));

    public static readonly StyledProperty<double> CurrentValueProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, double>(nameof(CurrentValue));

    public static readonly StyledProperty<bool> CurrentIsDefaultProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, bool>(nameof(CurrentIsDefault));

    public static readonly StyledProperty<double> SavedValueProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, double>(nameof(SavedValue));

    public static readonly StyledProperty<bool> SavedIsDefaultProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, bool>(nameof(SavedIsDefault));

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, string>(nameof(EmptyText), "No scoring preview yet");

    public static readonly StyledProperty<string> AxisValueSuffixProperty =
        AvaloniaProperty.Register<ScoringAdjustmentGraphControl, string>(nameof(AxisValueSuffix), string.Empty);

    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    private IBrush DefaultBackgroundBrush => new SolidColorBrush(
        ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light
            ? Color.FromArgb(24, 133, 81, 0)
            : Color.FromArgb(22, 255, 198, 92));

    static ScoringAdjustmentGraphControl()
    {
        AffectsRender<ScoringAdjustmentGraphControl>(
            SeriesProperty,
            CurrentValueProperty,
            CurrentIsDefaultProperty,
            SavedValueProperty,
            SavedIsDefaultProperty,
            AxisValueSuffixProperty,
            EmptyTextProperty);
    }

    public IReadOnlyList<ScoringAdjustmentSweepSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public double CurrentValue
    {
        get => GetValue(CurrentValueProperty);
        set => SetValue(CurrentValueProperty, value);
    }

    public bool CurrentIsDefault
    {
        get => GetValue(CurrentIsDefaultProperty);
        set => SetValue(CurrentIsDefaultProperty, value);
    }

    public double SavedValue
    {
        get => GetValue(SavedValueProperty);
        set => SetValue(SavedValueProperty, value);
    }

    public bool SavedIsDefault
    {
        get => GetValue(SavedIsDefaultProperty);
        set => SetValue(SavedIsDefaultProperty, value);
    }

    public string EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public string AxisValueSuffix
    {
        get => GetValue(AxisValueSuffixProperty);
        set => SetValue(AxisValueSuffixProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds;
        if (bounds.Width < 140 || bounds.Height < 110)
            return;

        Rect plot = GetPlotBounds(bounds);
        Rect defaultStrip = new(plot.Left + 5, plot.Top, 42, plot.Height);
        Rect rangePlot = new(plot.Left + 66, plot.Top, Math.Max(1, plot.Width - 66), plot.Height);

        Pen axisPen = new(AxisBrush, 1);
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        DrawHorizontalGrid(context, plot);

        IReadOnlyList<ScoringAdjustmentSweepSeries> series = Series ?? [];
        if (series.Count == 0)
        {
            DrawText(context, EmptyText, 12, TextBrush, new Point(plot.Left + 12, plot.Top + 12));
            return;
        }

        DrawSavedBaseline(context, plot, series);
        DrawDefaultStrip(context, defaultStrip, series);
        DrawRangeLines(context, rangePlot, series);
        DrawAverageRangeLine(context, rangePlot, series);
        DrawAxisLabels(context, plot, defaultStrip, rangePlot, series, AxisValueSuffix);
        DrawMarkers(context, plot, defaultStrip, rangePlot, series);
    }

    private void DrawSavedBaseline(DrawingContext context, Rect plot, IReadOnlyList<ScoringAdjustmentSweepSeries> series)
    {
        float savedAverage = series.Average(static item => item.SavedAccuracy);
        double y = GetY(plot, savedAverage);
        context.DrawLine(new Pen(MutedTextBrush, 1), new Point(plot.Left, y), new Point(plot.Right, y));
        DrawText(context, "saved", 10, MutedTextBrush, new Point(plot.Right - 34, y - 14));
    }

    private void DrawDefaultStrip(DrawingContext context, Rect strip, IReadOnlyList<ScoringAdjustmentSweepSeries> series)
    {
        context.DrawRectangle(DefaultBackgroundBrush, null, strip);
        context.DrawLine(new Pen(GridBrush, 1), new Point(strip.Right + 8, strip.Top), new Point(strip.Right + 8, strip.Bottom));

        List<float> defaultValues = [];
        foreach (ScoringAdjustmentSweepSeries item in series)
        {
            ScoringAdjustmentSweepPoint? point = item.DefaultPoint;
            if (point == null)
                continue;

            defaultValues.Add(point.Accuracy);
            Color color = GetSeriesColor(item.SeriesIndex, 170);
            Pen pen = new(new SolidColorBrush(color), 1.5);
            double y = GetY(strip, point.Accuracy);
            double center = strip.Left + (strip.Width / 2.0);
            context.DrawLine(pen, new Point(center - 13, y), new Point(center + 13, y));
        }

        if (defaultValues.Count > 0)
        {
            double y = GetY(strip, defaultValues.Average());
            context.DrawLine(new Pen(TextBrush, 2.5), new Point(strip.Left + 5, y), new Point(strip.Right - 5, y));
            context.DrawEllipse(TextBrush, null, new Point(strip.Left + (strip.Width / 2.0), y), 3.5, 3.5);
        }
    }

    private void DrawRangeLines(
        DrawingContext context,
        Rect rangePlot,
        IReadOnlyList<ScoringAdjustmentSweepSeries> series)
    {
        (double min, double max) = ResolveRange(series);
        foreach (ScoringAdjustmentSweepSeries item in series)
        {
            IReadOnlyList<ScoringAdjustmentSweepPoint> points = item.RangePoints;
            if (points.Count == 0)
                continue;

            Color color = GetSeriesColor(item.SeriesIndex, 125);
            Pen pen = new(new SolidColorBrush(color), 1.25);
            List<Point> screenPoints = [.. points.Select(point => new Point(GetX(rangePlot, min, max, point.Value), GetY(rangePlot, point.Accuracy)))];
            DrawPolyline(context, screenPoints, pen);
        }
    }

    private void DrawAverageRangeLine(
        DrawingContext context,
        Rect rangePlot,
        IReadOnlyList<ScoringAdjustmentSweepSeries> series)
    {
        List<ScoringAdjustmentSweepPoint> averagePoints = GetAverageRangePoints(series);
        if (averagePoints.Count == 0)
            return;

        (double min, double max) = ResolveRange(series);
        List<Point> screenPoints = [.. averagePoints.Select(point => new Point(GetX(rangePlot, min, max, point.Value), GetY(rangePlot, point.Accuracy)))];
        DrawPolyline(context, screenPoints, new Pen(TextBrush, 2.5));
    }

    private void DrawMarkers(
        DrawingContext context,
        Rect plot,
        Rect defaultStrip,
        Rect rangePlot,
        IReadOnlyList<ScoringAdjustmentSweepSeries> series)
    {
        Point savedMarker = GetAverageMarker(plot, defaultStrip, rangePlot, series, SavedValue, SavedIsDefault, useSavedAverage: true);
        context.DrawEllipse(MutedTextBrush, null, savedMarker, 3.5, 3.5);

        Point currentMarker = GetAverageMarker(plot, defaultStrip, rangePlot, series, CurrentValue, CurrentIsDefault, useSavedAverage: false);
        context.DrawLine(new Pen(TextBrush, 1), new Point(currentMarker.X, plot.Top), new Point(currentMarker.X, plot.Bottom));
        context.DrawEllipse(TextBrush, new Pen(AccentBrush, 2), currentMarker, 5.0, 5.0);
    }

    private static Point GetAverageMarker(
        Rect plot,
        Rect defaultStrip,
        Rect rangePlot,
        IReadOnlyList<ScoringAdjustmentSweepSeries> series,
        double value,
        bool isDefault,
        bool useSavedAverage)
    {
        if (useSavedAverage)
        {
            double savedY = GetY(plot, series.Average(static item => item.SavedAccuracy));
            double savedX = isDefault
                ? defaultStrip.Left + (defaultStrip.Width / 2.0)
                : GetRangeX(rangePlot, series, value);
            return new Point(savedX, savedY);
        }

        if (isDefault)
        {
            List<float> defaultValues = [.. series
                .Select(static item => item.DefaultPoint?.Accuracy)
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)];
            float defaultAverage = defaultValues.Count == 0 ? 0.0f : defaultValues.Average();
            return new Point(defaultStrip.Left + (defaultStrip.Width / 2.0), GetY(defaultStrip, defaultAverage));
        }

        float average = EstimateAverageAt(series, value);
        return new Point(GetRangeX(rangePlot, series, value), GetY(rangePlot, average));
    }

    private void DrawHorizontalGrid(DrawingContext context, Rect plot)
    {
        for (int i = 0; i <= 4; i++)
        {
            double ratio = i / 4.0;
            double y = plot.Bottom - (plot.Height * ratio);
            context.DrawLine(new Pen(GridBrush, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(context, (100.0 * ratio).ToString("0", CultureInfo.InvariantCulture), 10, TextBrush, new Point(6, y - 7));
        }
    }

    private void DrawAxisLabels(
        DrawingContext context,
        Rect plot,
        Rect defaultStrip,
        Rect rangePlot,
        IReadOnlyList<ScoringAdjustmentSweepSeries> series,
        string valueSuffix)
    {
        DrawText(context, "Default", 10, MutedTextBrush, new Point(defaultStrip.Left - 2, plot.Bottom + 5));

        (double min, double max) = ResolveRange(series);
        for (int i = 0; i <= 4; i++)
        {
            double ratio = i / 4.0;
            double value = min + ((max - min) * ratio);
            double x = rangePlot.Left + (rangePlot.Width * ratio);
            string label = value.ToString("0.###", CultureInfo.InvariantCulture) + valueSuffix;
            DrawText(context, label, 10, MutedTextBrush, new Point(x - 10, plot.Bottom + 5));
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

    private static List<ScoringAdjustmentSweepPoint> GetAverageRangePoints(IReadOnlyList<ScoringAdjustmentSweepSeries> series)
        => [.. series
            .SelectMany(static item => item.RangePoints)
            .GroupBy(static point => point.Value)
            .OrderBy(static group => group.Key)
            .Select(static group => new ScoringAdjustmentSweepPoint(
                group.Key,
                group.Average(static point => point.Accuracy),
                IsDefaultValue: false))];

    private static float EstimateAverageAt(IReadOnlyList<ScoringAdjustmentSweepSeries> series, double value)
    {
        List<float> values = [];
        foreach (ScoringAdjustmentSweepSeries item in series)
        {
            ScoringAdjustmentSweepPoint? point = EstimatePointAt(item.RangePoints, value);
            if (point != null)
                values.Add(point.Accuracy);
        }

        return values.Count == 0 ? 0.0f : values.Average();
    }

    private static ScoringAdjustmentSweepPoint? EstimatePointAt(IReadOnlyList<ScoringAdjustmentSweepPoint> points, double value)
    {
        if (points.Count == 0)
            return null;

        ScoringAdjustmentSweepPoint lower = points[0];
        ScoringAdjustmentSweepPoint upper = points[^1];
        foreach (ScoringAdjustmentSweepPoint point in points)
        {
            if (point.Value <= value)
                lower = point;
            if (point.Value >= value)
            {
                upper = point;
                break;
            }
        }

        if (Math.Abs(upper.Value - lower.Value) < 0.000001)
            return lower;

        double ratio = (value - lower.Value) / (upper.Value - lower.Value);
        return new ScoringAdjustmentSweepPoint(
            value,
            (float)(lower.Accuracy + ((upper.Accuracy - lower.Accuracy) * ratio)),
            IsDefaultValue: false);
    }

    private static Rect GetPlotBounds(Rect bounds)
        => new(48, 12, Math.Max(1, bounds.Width - 62), Math.Max(1, bounds.Height - 42));

    private static (double Min, double Max) ResolveRange(IReadOnlyList<ScoringAdjustmentSweepSeries> series)
    {
        IReadOnlyList<ScoringAdjustmentSweepPoint> points = [.. series.SelectMany(static item => item.RangePoints)];
        if (points.Count == 0)
            return (0.0, 1.0);

        double min = points.Min(static point => point.Value);
        double max = points.Max(static point => point.Value);
        if (max <= min)
            max = min + 1.0;

        return (min, max);
    }

    private static double GetRangeX(Rect rangePlot, IReadOnlyList<ScoringAdjustmentSweepSeries> series, double value)
    {
        (double min, double max) = ResolveRange(series);
        return GetX(rangePlot, min, max, Math.Clamp(value, min, max));
    }

    private static double GetX(Rect plot, double min, double max, double value)
        => plot.Left + ((value - min) / (max - min) * plot.Width);

    private static double GetY(Rect plot, double value)
        => plot.Bottom - (Math.Clamp(value, 0.0, 100.0) / 100.0 * plot.Height);

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point point)
    {
        FormattedText formatted = new(text, Culture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, point);
    }
}