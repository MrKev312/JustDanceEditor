using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    // Cache clip-specific brushes to avoid recreating them frequently
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = [];

    private SolidColorBrush GetOrCreateBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[color] = brush;
        }
        return brush;
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;

        double visibleStartBeat = offset - 1;
        double visibleEndBeat = offset + (bounds.Width / System.Math.Max(1.0, ppb)) + 1;

        if (Background != null)
            context.FillRectangle(Background, new Rect(bounds.Size));

        // Marker lines
        if (BeatOffset != 0)
        {
            double x0 = -offset * ppb;
            if (x0 > -1 && x0 < bounds.Width + 1)
                context.DrawLine(_linePen, new Point(x0, 0), new Point(x0, bounds.Height));
        }
        else
            context.DrawLine(_linePen, new Point(0, 0), new Point(0, bounds.Height));

        if (MaxBeat > 0)
        {
            double xEnd = MaxBeat * ppb;
            if (xEnd > -1 && xEnd < bounds.Width + 1)
                context.DrawLine(_linePen, new Point(xEnd, 0), new Point(xEnd, bounds.Height));
        }

        // Draw box selection if active even when Clips is null
        if (_boxSelectionHandler?.IsActive == true)
        {
            var x = System.Math.Min(_boxSelectionHandler.StartPoint.X, _boxSelectionHandler.CurrentPoint.X);
            var y = System.Math.Min(_boxSelectionHandler.StartPoint.Y, _boxSelectionHandler.CurrentPoint.Y);
            var w = System.Math.Abs(_boxSelectionHandler.CurrentPoint.X - _boxSelectionHandler.StartPoint.X);
            var h = System.Math.Abs(_boxSelectionHandler.CurrentPoint.Y - _boxSelectionHandler.StartPoint.Y);

            var rect = new Rect(x, y, w, h);
            context.FillRectangle(_boxSelectionFill, rect);
            context.DrawRectangle(null, _boxSelectionBorderPen, rect);
        }

        if (Clips == null)
            return;

        bool drawText = ppb > 10;

        foreach (ClipViewModel clip in Clips)
        {
            double clipStart = clip.StartBeat;
            double clipEnd = clip.StartBeat + clip.DurationBeats;

            if (clipEnd < visibleStartBeat || clipStart > visibleEndBeat)
                continue;

            double startX = (clipStart - offset) * ppb;
            double width = clip.DurationBeats * ppb;
            double endX = startX + width;

            if (endX < 0 || startX > bounds.Width)
                continue;

            var rect = new Rect(startX, 2, System.Math.Max(0, width), System.Math.Max(1, bounds.Height - 4));

            // Use cached brush for clip background
            var clipBrush = GetOrCreateBrush(clip.BackgroundColor);
            context.FillRectangle(clipBrush, rect);

            // Use cached outline pen with computed thickness
            var outlineThickness = System.Math.Max(1.0, rect.Height * 0.05);
            var outlinePen = (outlineThickness == 1.0)
                ? _blackOutlinePen
                : new Pen(Brushes.Black, outlineThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawRectangle(null, outlinePen, rect);

            // Selection visual
            if (clip.IsSelected)
            {
                // Slight overlay and gold outline
                context.FillRectangle(_selectionOverlay, rect);
                context.DrawRectangle(null, _selectionPen, rect.Deflate(1));
            }

            if (clip.ImagePath != null)
            {
                if (BitmapCache.TryGet(clip.ImagePath, out Bitmap? bmp) && bmp != null)
                {
                    var aspect = bmp.Size.Width / (double)bmp.Size.Height;
                    var drawHeight = rect.Height;
                    var drawWidth = drawHeight * aspect;

                    if (drawWidth >= 2 && drawHeight >= 2)
                    {
                        var imgX = startX + ((width - drawWidth) / 2);
                        var imgY = rect.Y;
                        var destRect = new Rect(imgX, imgY, drawWidth, drawHeight);
                        using (context.PushClip(rect))
                        {
                            context.DrawImage(bmp, new Rect(bmp.Size), destRect);
                        }
                    }
                }
                else
                {
                    BitmapCache.ScheduleLoad(clip.ImagePath, InvalidateVisual);
                }
            }
            else if (drawText && width > 30 && !string.IsNullOrEmpty(clip.Name))
            {
                FormattedText ft = GetFormattedText(clip, clip.Name, 12, width, rect.Height);
                var textX = startX + ((width - ft.Width) / 2);
                var textY = rect.Y + ((rect.Height - ft.Height) / 2);
                if (textX < startX)
                    textX = startX;
                context.DrawText(ft, new Point(textX, textY));
            }
        }
    }
}