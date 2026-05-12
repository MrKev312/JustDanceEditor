using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;

using RenderingHelpers = KevInc.Avalonia.Rendering.RenderingHelpers;
using TimelineRenderHelper = KevInc.Avalonia.Timeline.TimelineRenderHelper;
using TimelineResources = KevInc.Avalonia.Timeline.TimelineResources;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel
{
    // Cache clip-specific brushes to avoid recreating them frequently
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = [];

    private SolidColorBrush GetOrCreateBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out SolidColorBrush? brush))
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
        double visibleEndBeat = offset + (bounds.Width / Math.Max(1.0, ppb)) + 1;

        if (Background != null)
            context.FillRectangle(Background, new Rect(bounds.Size));

        // Draw alternating measure backgrounds
        DrawMeasureBackgrounds(context, bounds, ppb, offset, visibleStartBeat, visibleEndBeat);

        // Marker lines
        if (BeatOffset != 0)
        {
            double x0 = -offset * ppb;
            if (x0 > -1 && x0 < bounds.Width + 1)
                context.DrawLine(TimelineResources.LinePen, new Point(x0, 0), new Point(x0, bounds.Height));
        }
        else
            context.DrawLine(TimelineResources.LinePen, new Point(0, 0), new Point(0, bounds.Height));

        if (MaxBeat > 0)
        {
            double xEnd = MaxBeat * ppb;
            if (xEnd > -1 && xEnd < bounds.Width + 1)
                context.DrawLine(TimelineResources.LinePen, new Point(xEnd, 0), new Point(xEnd, bounds.Height));
        }

        // Draw beat/measure grid lines (behind clips)
        DrawGridLines(context, bounds, ppb, offset, visibleStartBeat, visibleEndBeat);

        // Draw box selection if active even when Clips is null
        if (_boxSelectionHandler?.IsActive == true)
        {
            double x = Math.Min(_boxSelectionHandler.StartPoint.X, _boxSelectionHandler.CurrentPoint.X);
            double y = Math.Min(_boxSelectionHandler.StartPoint.Y, _boxSelectionHandler.CurrentPoint.Y);
            double w = Math.Abs(_boxSelectionHandler.CurrentPoint.X - _boxSelectionHandler.StartPoint.X);
            double h = Math.Abs(_boxSelectionHandler.CurrentPoint.Y - _boxSelectionHandler.StartPoint.Y);

            Rect rect = new(x, y, w, h);
            context.FillRectangle(TimelineResources.BoxSelectionFill, rect);
            context.DrawRectangle(null, TimelineResources.BoxSelectionBorderPen, rect);
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

            Rect rect = new(startX, 2, Math.Max(0, width), Math.Max(1, bounds.Height - 4));

            // Use cached brush for clip background (use RenderColor source-of-truth)
            SolidColorBrush clipBrush = GetOrCreateBrush(clip.RenderColor);
            context.FillRectangle(clipBrush, rect);

            // If this clip represents a move whose asset file was missing, overlay
            // diagonal stripes to warn the user.  We use the generic helper so the
            // appearance matches the waveform "no audio" stripes elsewhere.
            if (clip is MoveClipViewModel mv && mv.IsAssetMissing)
            {
                RenderingHelpers.OverlayStripes(context, rect, clip.RenderColor);
            }

            // Create darker outline from clip color
            Color outlineColor = DarkenColor(clip.RenderColor, 0.6);
            SolidColorBrush outlineBrush = GetOrCreateBrush(outlineColor);
            double outlineThickness = Math.Max(1.0, rect.Height * 0.05);
            Pen outlinePen = new(outlineBrush, outlineThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawRectangle(null, outlinePen, rect);

            // Selection visual
            if (clip.IsSelected)
            {
                // Slight overlay and gold outline
                context.FillRectangle(TimelineResources.SelectionOverlay, rect);
                context.DrawRectangle(null, TimelineResources.SelectionPen, rect.Deflate(1));
            }

            if (clip.ImagePath != null)
            {
                if (ImageBitmapCache.TryGet(clip.ImagePath, out Bitmap? bmp) && bmp != null)
                {
                    double aspect = bmp.Size.Width / (double)bmp.Size.Height;
                    double drawHeight = rect.Height;
                    double drawWidth = drawHeight * aspect;

                    if (drawWidth >= 2 && drawHeight >= 2)
                    {
                        double imgX = startX + ((width - drawWidth) / 2);
                        double imgY = rect.Y;
                        Rect destRect = new(imgX, imgY, drawWidth, drawHeight);
                        using (context.PushClip(rect))
                        {
                            context.DrawImage(bmp, new Rect(bmp.Size), destRect);
                        }
                    }
                }
                else
                {
                    ImageBitmapCache.ScheduleLoad(clip.ImagePath, InvalidateVisual);
                }
            }
            else if (drawText && width > 30 && !string.IsNullOrEmpty(clip.Name))
            {
                FormattedText ft = GetFormattedText(clip, clip.Name, 12, width, rect.Height);
                double textX = startX + ((width - ft.Width) / 2);
                double textY = rect.Y + ((rect.Height - ft.Height) / 2);
                if (textX < startX)
                    textX = startX;
                context.DrawText(ft, new Point(textX, textY));
            }
        }
    }

    private static Color DarkenColor(Color color, double factor)
    {
        // factor should be between 0 and 1, where 1 is fully darkened to black
        byte r = (byte)(color.R * (1 - factor));
        byte g = (byte)(color.G * (1 - factor));
        byte b = (byte)(color.B * (1 - factor));
        return new Color(color.A, r, g, b);
    }

    private void DrawMeasureBackgrounds(DrawingContext context, Rect bounds, double ppb, int offset, double visibleStartBeat, double visibleEndBeat)
    {
        List<SignatureSegment> sortedSigs = Signatures?.OrderBy(s => s.Marker).ToList() ?? [];
        List<double> sectionStarts = Sections != null
            ? [.. Sections.OrderBy(s => s.StartBeat).Select(s => (double)s.StartBeat)]
            : [];

        SolidColorBrush brushA = new(Colors.White, 0.05);
        SolidColorBrush brushB = new(Colors.White, 0.02);
        SolidColorBrush brushErr = new(Colors.Red, 0.08);

        double rangeStart = offset;
        double rangeEnd = offset + MaxBeat;

        List<(double start, double end)> intervals = [];
        if (sectionStarts.Count == 0)
        {
            intervals.Add((rangeStart, rangeEnd));
        }
        else
        {
            for (int i = 0; i < sectionStarts.Count; i++)
            {
                double sStart = sectionStarts[i];
                double sEnd = (i + 1 < sectionStarts.Count) ? sectionStarts[i + 1] : rangeEnd;
                intervals.Add((sStart, sEnd));
            }
        }

        int colorIndex = 0;
        for (int si = 0; si < intervals.Count; si++)
        {
            bool isLastSection = si == intervals.Count - 1;
            (double sStart, double sEnd) = intervals[si];

            if (sEnd <= rangeStart)
            {
                colorIndex += TimelineRenderHelper.CountAllGroups(sStart, sEnd, sortedSigs);
                continue;
            }

            if (sStart >= rangeEnd)
                break;

            int sectionColor = colorIndex;
            int groupInSection = 0;
            double pos = sStart;

            while (pos < sEnd - 0.01 && pos < rangeEnd)
            {
                int blockSize = TimelineRenderHelper.GetActiveBlockSize(pos, sortedSigs);
                double gEnd = pos + blockSize;

                bool isPartialSectionEnd = gEnd > sEnd + 0.01;
                if (isPartialSectionEnd)
                    gEnd = sEnd;

                double nextSig = TimelineRenderHelper.GetNextSigChange(pos, sortedSigs);
                bool isPartialSigChange = false;
                if (!isPartialSectionEnd && nextSig < gEnd - 0.01)
                {
                    gEnd = nextSig;
                    isPartialSigChange = true;
                }

                bool isPartial = isPartialSigChange || (isPartialSectionEnd && !isLastSection);

                double xStart = (pos - offset) * ppb;
                double xEnd = (gEnd - offset) * ppb;
                if (xEnd >= 0 && xStart <= bounds.Width)
                {
                    SolidColorBrush brush = isPartial ? brushErr : ((sectionColor + groupInSection) % 2 == 0 ? brushA : brushB);
                    context.FillRectangle(brush, new Rect(xStart, 0, xEnd - xStart, bounds.Height));
                }

                groupInSection++;
                pos = gEnd;
            }

            colorIndex += groupInSection;
        }
    }

    private void DrawGridLines(DrawingContext context, Rect bounds, double ppb, int offset, double visibleStartBeat, double visibleEndBeat)
    {
        List<double> sectionStarts = Sections != null
            ? [.. Sections.OrderBy(s => s.StartBeat).Select(s => (double)s.StartBeat)]
            : [];

        for (int beat = (int)visibleStartBeat; beat <= (int)visibleEndBeat; beat++)
        {
            double x = (beat - offset) * ppb;
            if (x < 0 || x > bounds.Width)
                continue;

            bool isMeasure = TimelineRenderHelper.IsBaseGroupBeat(beat, sectionStarts);
            if (isMeasure)
                context.DrawLine(TimelineResources.MeasureGridPen, new Point(x, 0), new Point(x, bounds.Height));
            else
                context.DrawLine(TimelineResources.BeatGridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
    }
}
