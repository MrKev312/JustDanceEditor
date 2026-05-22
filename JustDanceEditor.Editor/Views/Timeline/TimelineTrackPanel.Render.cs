using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

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
    private const double ViewportRenderPadding = 64;
    private const double MergeClipsBelowPixelsPerBeat = 8;
    private const double MergeGapTolerancePixels = 1.5;
    private const double MinimumDetailedClipWidth = 6;
    private const double MinimumImageClipWidth = 28;
    private const double MinimumTextClipWidth = 40;
    private static readonly SolidColorBrush MeasureBrushA = new(Colors.White, 0.05);
    private static readonly SolidColorBrush MeasureBrushB = new(Colors.White, 0.02);
    private static readonly SolidColorBrush MeasureBrushError = new(Colors.Red, 0.08);

    // Cache clip-specific brushes to avoid recreating them frequently
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = [];
    private readonly Dictionary<(Color color, double thickness), Pen> _penCache = [];

    private SolidColorBrush GetOrCreateBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out SolidColorBrush? brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[color] = brush;
        }

        return brush;
    }

    private Pen GetOrCreatePen(Color color, double thickness)
    {
        thickness = Math.Round(thickness, 2);
        (Color color, double thickness) key = (color, thickness);
        if (!_penCache.TryGetValue(key, out Pen? pen))
        {
            pen = new Pen(GetOrCreateBrush(color), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            _penCache[key] = pen;
        }

        return pen;
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;

        (double visiblePixelStart, double visiblePixelEnd) = GetVisiblePixelRange(bounds.Width);
        Rect renderBounds = new(visiblePixelStart, 0, Math.Max(0, visiblePixelEnd - visiblePixelStart), bounds.Height);
        double visibleStartBeat = offset + (visiblePixelStart / Math.Max(1.0, ppb)) - 1;
        double visibleEndBeat = offset + (visiblePixelEnd / Math.Max(1.0, ppb)) + 1;

        if (Background != null)
            context.FillRectangle(Background, renderBounds);

        // Draw alternating measure backgrounds
        DrawMeasureBackgrounds(context, bounds, ppb, offset, visibleStartBeat, visibleEndBeat, visiblePixelStart, visiblePixelEnd);

        // Marker lines
        if (BeatOffset != 0)
        {
            double x0 = -offset * ppb;
            if (x0 > visiblePixelStart - 1 && x0 < visiblePixelEnd + 1)
                context.DrawLine(TimelineResources.LinePen, new Point(x0, 0), new Point(x0, bounds.Height));
        }
        else
            context.DrawLine(TimelineResources.LinePen, new Point(0, 0), new Point(0, bounds.Height));

        if (MaxBeat > 0)
        {
            double xEnd = MaxBeat * ppb;
            if (xEnd > visiblePixelStart - 1 && xEnd < visiblePixelEnd + 1)
                context.DrawLine(TimelineResources.LinePen, new Point(xEnd, 0), new Point(xEnd, bounds.Height));
        }

        // Draw beat/measure grid lines (behind clips)
        DrawGridLines(context, bounds, ppb, offset, visibleStartBeat, visibleEndBeat, visiblePixelStart, visiblePixelEnd);

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
        bool drawImages = ppb > 8;
        if (ppb < MergeClipsBelowPixelsPerBeat)
        {
            DrawMergedClipBlocks(context, bounds, ppb, offset, visibleStartBeat, visibleEndBeat, visiblePixelStart, visiblePixelEnd);
            return;
        }

        foreach (ClipViewModel clip in Clips)
        {
            double clipStart = clip.StartBeat;
            double clipEnd = clip.StartBeat + clip.DurationBeats;

            if (clipEnd < visibleStartBeat || clipStart > visibleEndBeat)
                continue;

            double startX = (clipStart - offset) * ppb;
            double width = clip.DurationBeats * ppb;
            double endX = startX + width;

            if (endX < visiblePixelStart || startX > visiblePixelEnd)
                continue;

            Rect rect = new(startX, 2, Math.Max(0, width), Math.Max(1, bounds.Height - 4));
            if (rect.Width <= 0)
                continue;

            // Use cached brush for clip background (use RenderColor source-of-truth)
            SolidColorBrush clipBrush = GetOrCreateBrush(clip.RenderColor);
            context.FillRectangle(clipBrush, rect);
            bool drawDetails = width >= MinimumDetailedClipWidth;

            // If this clip represents a move whose asset file was missing, overlay
            // diagonal stripes to warn the user.  We use the generic helper so the
            // appearance matches the waveform "no audio" stripes elsewhere.
            if (drawDetails && clip is MoveClipViewModel mv && mv.IsAssetMissing)
            {
                RenderingHelpers.OverlayStripes(context, rect, clip.RenderColor);
            }

            // Create darker outline from clip color
            if (drawDetails)
            {
                Color outlineColor = DarkenColor(clip.RenderColor, 0.6);
                double outlineThickness = Math.Max(1.0, rect.Height * 0.05);
                context.DrawRectangle(null, GetOrCreatePen(outlineColor, outlineThickness), rect);
            }

            // Selection visual
            if (clip.IsSelected)
            {
                // Slight overlay and gold outline
                context.FillRectangle(TimelineResources.SelectionOverlay, rect);
                context.DrawRectangle(null, TimelineResources.SelectionPen, rect.Deflate(1));
            }

            if (drawDetails && drawImages && width >= MinimumImageClipWidth && clip.ImagePath != null)
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
            else if (drawText && width >= MinimumTextClipWidth && !string.IsNullOrEmpty(clip.Name))
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

    private (double Start, double End) GetVisiblePixelRange(double totalWidth)
    {
        double start = 0;
        double end = totalWidth;

        if (_parentScrollViewer != null && _parentScrollViewer.Viewport.Width > 0)
        {
            start = Math.Max(0, _parentScrollViewer.Offset.X - ViewportRenderPadding);
            end = Math.Min(totalWidth, _parentScrollViewer.Offset.X + _parentScrollViewer.Viewport.Width + ViewportRenderPadding);
        }

        return (start, Math.Max(start, end));
    }

    private void DrawMergedClipBlocks(
        DrawingContext context,
        Rect bounds,
        double ppb,
        int offset,
        double visibleStartBeat,
        double visibleEndBeat,
        double visiblePixelStart,
        double visiblePixelEnd)
    {
        if (Clips == null)
            return;

        bool hasGroup = false;
        double groupStartX = 0;
        double groupEndX = 0;
        Color groupColor = Colors.Transparent;
        bool groupSelected = false;

        foreach (ClipViewModel clip in GetSortedClips())
        {
            double clipStart = clip.StartBeat;
            double clipEnd = clipStart + clip.DurationBeats;

            if (clipEnd < visibleStartBeat)
                continue;
            if (clipStart > visibleEndBeat)
                break;

            double startX = (clipStart - offset) * ppb;
            double endX = startX + clip.DurationBeats * ppb;
            if (endX < visiblePixelStart || startX > visiblePixelEnd)
                continue;

            if (!hasGroup)
            {
                StartGroup(startX, endX, clip.RenderColor, clip.IsSelected);
                continue;
            }

            bool touchesGroup = startX <= groupEndX + MergeGapTolerancePixels;
            bool compatibleSelection = clip.IsSelected == groupSelected;
            if (!touchesGroup || !compatibleSelection)
            {
                FlushGroup();
                StartGroup(startX, endX, clip.RenderColor, clip.IsSelected);
                continue;
            }

            groupEndX = Math.Max(groupEndX, endX);
        }

        FlushGroup();

        void StartGroup(double startX, double endX, Color color, bool selected)
        {
            hasGroup = true;
            groupStartX = startX;
            groupEndX = endX;
            groupColor = color;
            groupSelected = selected;
        }

        void FlushGroup()
        {
            if (!hasGroup)
                return;

            double clippedStart = Math.Max(groupStartX, visiblePixelStart);
            double clippedEnd = Math.Min(groupEndX, visiblePixelEnd);
            if (clippedEnd > clippedStart)
            {
                Rect rect = new(clippedStart, 2, clippedEnd - clippedStart, Math.Max(1, bounds.Height - 4));
                context.FillRectangle(GetOrCreateBrush(groupColor), rect);
                if (groupSelected)
                {
                    context.FillRectangle(TimelineResources.SelectionOverlay, rect);
                    context.DrawRectangle(null, TimelineResources.SelectionPen, rect.Deflate(1));
                }
            }

            hasGroup = false;
        }
    }

    private IReadOnlyList<ClipViewModel> GetSortedClips()
    {
        if (!_sortedClipCacheDirty)
            return _sortedClipCache;

        _sortedClipCache.Clear();
        if (Clips != null)
        {
            foreach (ClipViewModel clip in Clips)
                _sortedClipCache.Add(clip);

            _sortedClipCache.Sort(CompareClipsByStartBeat);
        }

        _sortedClipCacheDirty = false;
        return _sortedClipCache;
    }

    private void DrawMeasureBackgrounds(
        DrawingContext context,
        Rect bounds,
        double ppb,
        int offset,
        double visibleStartBeat,
        double visibleEndBeat,
        double visiblePixelStart,
        double visiblePixelEnd)
    {
        IReadOnlyList<SignatureSegment> sortedSigs = GetSortedSignatures();
        List<double> sectionStarts = GetSectionStarts();

        double rangeStart = Math.Max(offset, visibleStartBeat);
        double rangeEnd = Math.Min(offset + MaxBeat, visibleEndBeat);
        if (rangeEnd <= rangeStart)
            return;

        int colorIndex = 0;
        if (sectionStarts.Count == 0)
        {
            DrawSection(rangeStart, rangeEnd, isLastSection: true, ref colorIndex);
        }
        else
        {
            for (int i = 0; i < sectionStarts.Count; i++)
            {
                double sStart = sectionStarts[i];
                double sEnd = (i + 1 < sectionStarts.Count) ? sectionStarts[i + 1] : rangeEnd;
                DrawSection(sStart, sEnd, i == sectionStarts.Count - 1, ref colorIndex);
            }
        }

        void DrawSection(double sStart, double sEnd, bool isLastSection, ref int colorIndex)
        {
            if (sEnd <= rangeStart)
            {
                colorIndex += TimelineRenderHelper.CountAllGroups(sStart, sEnd, sortedSigs);
                return;
            }

            if (sStart >= rangeEnd)
                return;

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
                if (xEnd >= visiblePixelStart && xStart <= visiblePixelEnd)
                {
                    SolidColorBrush brush = isPartial ? MeasureBrushError : ((sectionColor + groupInSection) % 2 == 0 ? MeasureBrushA : MeasureBrushB);
                    double clippedStart = Math.Max(xStart, visiblePixelStart);
                    double clippedEnd = Math.Min(xEnd, visiblePixelEnd);
                    context.FillRectangle(brush, new Rect(clippedStart, 0, clippedEnd - clippedStart, bounds.Height));
                }

                groupInSection++;
                pos = gEnd;
            }

            colorIndex += groupInSection;
        }
    }

    private void DrawGridLines(
        DrawingContext context,
        Rect bounds,
        double ppb,
        int offset,
        double visibleStartBeat,
        double visibleEndBeat,
        double visiblePixelStart,
        double visiblePixelEnd)
    {
        List<double> sectionStarts = GetSectionStarts();

        bool drawBeatLines = ppb >= 6;
        for (int beat = (int)visibleStartBeat; beat <= (int)visibleEndBeat; beat++)
        {
            double x = (beat - offset) * ppb;
            if (x < visiblePixelStart || x > visiblePixelEnd)
                continue;

            bool isMeasure = TimelineRenderHelper.IsBaseGroupBeat(beat, sectionStarts);
            if (isMeasure)
                context.DrawLine(TimelineResources.MeasureGridPen, new Point(x, 0), new Point(x, bounds.Height));
            else if (drawBeatLines)
                context.DrawLine(TimelineResources.BeatGridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
    }

    private IReadOnlyList<SignatureSegment> GetSortedSignatures()
    {
        if (!_signatureCacheDirty)
            return _sortedSignatureCache;

        _sortedSignatureCache.Clear();
        if (Signatures != null)
        {
            foreach (SignatureSegment signature in Signatures)
                _sortedSignatureCache.Add(signature);

            _sortedSignatureCache.Sort(CompareSignaturesByMarker);
        }

        _signatureCacheDirty = false;
        return _sortedSignatureCache;
    }

    private List<double> GetSectionStarts()
    {
        if (!_sectionCacheDirty)
            return _sectionStartCache;

        _sectionStartCache.Clear();
        if (Sections != null)
        {
            foreach (SectionSegment section in Sections)
                _sectionStartCache.Add(section.StartBeat);

            _sectionStartCache.Sort();
        }

        _sectionCacheDirty = false;
        return _sectionStartCache;
    }
}
