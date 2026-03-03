using Avalonia;
using Avalonia.Media;

using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views;

/// <summary>
/// Helper routines for drawing common patterns such as diagonal stripes.
/// We moved the "no audio" stripes code out of <see cref="AudioBarControl"/>
/// to make it reusable for other features (e.g. missing move assets).
/// </summary>
public static class RenderingHelpers
{
    // cache striped brushes so we don't recreate them constantly
    private static readonly Dictionary<Color, DrawingBrush> _stripedBrushCache = new();

    /// <summary>
    /// Draws diagonal stripes over <paramref name="region"/>.  If
    /// <paramref name="fillColor"/> has a nonzero alpha the region will first be
    /// filled with that color (typically a semi-transparent overlay).  White
    /// stripes are then drawn at 45° increments using <paramref name="stripeColor"/>.
    /// </summary>
    public static void DrawDiagonalStripes(
        DrawingContext context,
        Rect region,
        Color fillColor,
        Color stripeColor,
        double stripeWidth = 4.0,
        double stripeSpacing = 16.0)
    {
        if (region.Width <= 0 || region.Height <= 0)
            return;

        if (fillColor.A != 0)
        {
            context.FillRectangle(new SolidColorBrush(fillColor), region);
        }

        using (context.PushClip(region))
        {
            Pen stripePen = new(new SolidColorBrush(stripeColor), stripeWidth);
            double step = stripeSpacing;
            double x0 = region.X;
            double x1 = region.Right;
            double y0 = region.Y;
            double y1 = region.Bottom;
            double h = y1 - y0;

            for (double d = x0 - h; d < x1 + h; d += step)
            {
                context.DrawLine(stripePen,
                    new Point(d, y1),
                    new Point(d + h, y0));
            }
        }
    }

    /// <summary>
    /// Convenience overlay used for missing assets: lightly tint the underlying
    /// colour and then paint white stripes on top.  Clips use this so the
    /// original colour is still visible but marked as "broken".
    /// </summary>
    public static void OverlayStripes(DrawingContext context, Rect region, Color baseColor)
    {
        Color fill = new(60, baseColor.R, baseColor.G, baseColor.B);
        Color stripe = Color.FromArgb(100, 255, 255, 255);
        DrawDiagonalStripes(context, region, fill, stripe);
    }

    /// <summary>
    /// Returns a tiled brush that renders the diagonal stripe pattern over a
    /// solid <paramref name="baseColor"/>.  Used by library icons where the
    /// item is not rendered via custom drawing code.
    /// </summary>
    public static IBrush GetStripedBrush(Color baseColor)
    {
        if (_stripedBrushCache.TryGetValue(baseColor, out DrawingBrush? cached))
            return cached;

        double size = 16;
        DrawingGroup dg = new();

        // base fill
        dg.Children.Add(new GeometryDrawing
        {
            Brush = new SolidColorBrush(baseColor),
            Geometry = new RectangleGeometry(new Rect(0, 0, size, size))
        });

        // white stripes
        Pen stripePen = new(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 4);
        double h = size;
        for (double d = -h; d < size + h; d += size)
        {
            dg.Children.Add(new GeometryDrawing
            {
                Pen = stripePen,
                Geometry = new LineGeometry(new Point(d, h), new Point(d + h, 0))
            });
        }

        DrawingBrush brush = new(dg)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None
        };

        _stripedBrushCache[baseColor] = brush;
        return brush;
    }
}