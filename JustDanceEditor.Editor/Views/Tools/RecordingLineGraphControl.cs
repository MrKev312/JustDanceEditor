using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Tools;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class RecordingLineGraphControl : ThemeAwareGraphControl
{
    public static readonly StyledProperty<IReadOnlyList<RecordingGraphPoint>?> PointsProperty =
        AvaloniaProperty.Register<RecordingLineGraphControl, IReadOnlyList<RecordingGraphPoint>?>(nameof(Points));

    public static readonly StyledProperty<IReadOnlyList<RecordingGraphMarker>?> MarkersProperty =
        AvaloniaProperty.Register<RecordingLineGraphControl, IReadOnlyList<RecordingGraphMarker>?>(nameof(Markers));

    public static readonly StyledProperty<double> MinBeatProperty =
        AvaloniaProperty.Register<RecordingLineGraphControl, double>(nameof(MinBeat));

    public static readonly StyledProperty<double> MaxBeatProperty =
        AvaloniaProperty.Register<RecordingLineGraphControl, double>(nameof(MaxBeat), 1.0);

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<RecordingLineGraphControl, double>(nameof(MaxValue), 100.0);

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<RecordingLineGraphControl, string>(nameof(EmptyText), "No graph data");

    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    static RecordingLineGraphControl()
    {
        AffectsRender<RecordingLineGraphControl>(
            PointsProperty,
            MarkersProperty,
            MinBeatProperty,
            MaxBeatProperty,
            MaxValueProperty,
            EmptyTextProperty);
    }

    public IReadOnlyList<RecordingGraphPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IReadOnlyList<RecordingGraphMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public double MinBeat
    {
        get => GetValue(MinBeatProperty);
        set => SetValue(MinBeatProperty, value);
    }

    public double MaxBeat
    {
        get => GetValue(MaxBeatProperty);
        set => SetValue(MaxBeatProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
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
        if (bounds.Width < 80 || bounds.Height < 80)
            return;

        Rect plot = new(52, 12, Math.Max(1, bounds.Width - 66), Math.Max(1, bounds.Height - 42));
        Pen axisPen = new(AxisBrush, 1);
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        IReadOnlyList<RecordingGraphPoint> points = Points ?? [];
        double maxValue = Math.Max(1.0, MaxValue);
        double minBeat = MinBeat;
        double maxBeat = MaxBeat;
        if (maxBeat <= minBeat)
        {
            if (points.Count > 0)
            {
                minBeat = points.Min(static point => point.BeatLabel);
                maxBeat = points.Max(static point => point.BeatLabel);
            }

            if (maxBeat <= minBeat)
                maxBeat = minBeat + 1.0;
        }

        DrawHorizontalGrid(context, plot, maxValue);
        DrawBeatMarkers(context, plot, minBeat, maxBeat);

        if (points.Count == 0)
        {
            DrawText(context, EmptyText, 12, TextBrush, new Point(plot.Left + 12, plot.Top + 12));
            return;
        }

        List<Point> screenPoints = [.. points
            .OrderBy(static point => point.BeatLabel)
            .Select(point => new Point(
                GetX(plot, minBeat, maxBeat, point.BeatLabel),
                GetY(plot, maxValue, point.Value)))];

        if (screenPoints.Count == 1)
        {
            context.DrawEllipse(new SolidColorBrush(GetSeriesColor(1, byte.MaxValue)), null, screenPoints[0], 3, 3);
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(screenPoints[0], isFilled: false);
            for (int i = 1; i < screenPoints.Count; i++)
                g.LineTo(screenPoints[i]);
        }

        context.DrawGeometry(null, new Pen(AccentBrush, 2), geometry);

        SolidColorBrush pointBrush = new(GetSeriesColor(1, byte.MaxValue));
        foreach (Point point in screenPoints)
            context.DrawEllipse(pointBrush, null, point, 2.5, 2.5);
    }

    private void DrawHorizontalGrid(DrawingContext context, Rect plot, double maxValue)
    {
        for (int i = 0; i <= 4; i++)
        {
            double ratio = i / 4.0;
            double y = plot.Bottom - (plot.Height * ratio);
            context.DrawLine(new Pen(GridBrush, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(
                context,
                (maxValue * ratio).ToString(maxValue > 1000 ? "0" : "0.#", CultureInfo.InvariantCulture),
                10,
                TextBrush,
                new Point(6, y - 7));
        }
    }

    private void DrawBeatMarkers(DrawingContext context, Rect plot, double minBeat, double maxBeat)
    {
        IReadOnlyList<RecordingGraphMarker> markers = Markers ?? [];
        double lastGridX = double.NegativeInfinity;
        double lastLabelX = double.NegativeInfinity;

        foreach (RecordingGraphMarker marker in markers)
        {
            if (marker.BeatLabel < minBeat || marker.BeatLabel > maxBeat)
                continue;

            double x = GetX(plot, minBeat, maxBeat, marker.BeatLabel);
            if (x - lastGridX >= 18)
            {
                context.DrawLine(new Pen(GridBrush, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
                lastGridX = x;
            }

            if (x - lastLabelX >= 58)
            {
                DrawText(context, marker.Label, 10, TextBrush, new Point(x + 2, plot.Bottom + 4));
                lastLabelX = x;
            }
        }
    }

    private static double GetX(Rect plot, double minBeat, double maxBeat, double beat)
        => plot.Left + ((beat - minBeat) / (maxBeat - minBeat) * plot.Width);

    private static double GetY(Rect plot, double maxValue, double value)
    {
        double ratio = Math.Clamp(value / maxValue, 0.0, 1.0);
        return plot.Bottom - (ratio * plot.Height);
    }

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point point)
    {
        FormattedText formatted = new(
            text,
            Culture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        context.DrawText(formatted, point);
    }
}
