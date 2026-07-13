using Avalonia;
using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Reflection;

using RenderingHelpers = KevInc.Avalonia.Rendering.RenderingHelpers;
using TimelineRenderHelper = KevInc.Avalonia.Timeline.TimelineRenderHelper;
using TimelineResources = KevInc.Avalonia.Timeline.TimelineResources;

namespace JustDanceEditor.Editor.Views.Timeline;

internal sealed class AudioBarRenderer
{
    internal const int EnvelopeTileWidth = 512;
    private static readonly SolidColorBrush MeasureBrushError = new(Colors.Red, 0.08);
    private static readonly SolidColorBrush TooltipBackgroundBrush = new(Color.FromArgb(220, 30, 30, 30));
    private static readonly Pen TooltipBorderPen = new(new SolidColorBrush(Colors.White), 1);
    private static readonly Dictionary<SongSectionType, Color> SectionColors = [];
    private static readonly Dictionary<SongSectionType, SolidColorBrush> SectionBackgroundBrushes = [];

    private readonly Dictionary<SongSectionType, FormattedText> _sectionTextCache = [];
    private readonly Dictionary<int, FormattedText> _signatureTextCache = [];
    private double _lastPixelsPerBeat = -1;
    private Size _lastBounds = default;
    private readonly Dictionary<int, EnvelopeTile> _envelopeTiles = [];
    private int _envelopeCacheWidth;
    private float[]? _envelopeCacheSamples;
    private double _envelopeCacheAudioStartBeat = double.NaN;
    private double _envelopeCacheAudioEndBeat = double.NaN;
    private double _envelopeCacheBeatOffset = double.NaN;
    private double _envelopeCachePpb = double.NaN;
    private TimelineStructureDocument? _envelopeCacheTimelineStructure;

    public void InvalidateTimelineStructure()
    {
        _envelopeCacheTimelineStructure = null;
        _envelopeCacheSamples = null;
        _envelopeTiles.Clear();
    }

    static AudioBarRenderer()
    {
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
            ColorAttribute? attr = field?.GetCustomAttribute<ColorAttribute>();
            SectionColors[type] = attr != null
                ? Color.FromRgb(attr.R, attr.G, attr.B)
                : Colors.Gray;
            SectionBackgroundBrushes[type] = new SolidColorBrush(SectionColors[type], 0.3);
        }
    }

    public void Render(AudioBarRenderRequest request)
    {
        DrawingContext context = request.Context;
        Rect bounds = request.Bounds;
        double ppb = request.PixelsPerBeat;
        double offset = request.BeatOffset;
        double visiblePixelStart = request.VisiblePixelStart;
        double visiblePixelEnd = request.VisiblePixelEnd;
        double visibleStartBeat = offset + (visiblePixelStart / Math.Max(1.0, ppb)) - 1;
        double visibleEndBeat = offset + (visiblePixelEnd / Math.Max(1.0, ppb)) + 1;

        context.FillRectangle(Brushes.Transparent, bounds);
        request.SectionLabelRects.Clear();
        request.SignatureLabelRects.Clear();

        IReadOnlyList<SectionSegment> sortedSections = request.SortedSections;
        IReadOnlyList<SignatureSegment> sortedSignatures = request.SortedSignatures;
        List<double> sectionStarts = request.SectionStarts;

        if (sortedSections.Count > 0)
        {
            for (int i = 0; i < sortedSections.Count; i++)
            {
                SectionSegment section = sortedSections[i];
                double startX = (section.StartBeat - offset) * ppb;
                double endX = i + 1 < sortedSections.Count
                    ? (sortedSections[i + 1].StartBeat - offset) * ppb
                    : bounds.Width;

                if (startX < visiblePixelEnd && endX > visiblePixelStart)
                {
                    double clippedStart = Math.Max(visiblePixelStart, startX);
                    double clippedEnd = Math.Min(visiblePixelEnd, endX);
                    context.FillRectangle(
                        GetSectionBackgroundBrush(section.SectionType),
                        new Rect(clippedStart, 0, clippedEnd - clippedStart, bounds.Height));
                }
            }
        }

        double maxBeat = bounds.Width / Math.Max(1.0, ppb);
        DrawMeasureBackgrounds(context, bounds, ppb, (int)offset, maxBeat, visibleStartBeat, visibleEndBeat, visiblePixelStart, visiblePixelEnd, sortedSignatures, sectionStarts);
        DrawGridLines(context, bounds, ppb, (int)offset, visibleStartBeat, visibleEndBeat, visiblePixelStart, visiblePixelEnd, sectionStarts);
        DrawWaveform(request, visiblePixelStart, visiblePixelEnd);
        DrawLabelsAndTooltip(request, visiblePixelStart, visiblePixelEnd);
    }

    private void DrawWaveform(AudioBarRenderRequest request, double visiblePixelStart, double visiblePixelEnd)
    {
        float[]? samples = request.Samples;
        if (samples == null || samples.Length == 0)
            return;

        DrawingContext context = request.Context;
        Rect bounds = request.Bounds;
        double ppb = request.PixelsPerBeat;
        double offset = request.BeatOffset;
        int totalWidth = (int)bounds.Width;
        double centerY = bounds.Height / 2;
        double audioStartBeat = request.AudioStartBeat;
        double audioEndBeat = request.AudioEndBeat;
        bool boundsKnown = !double.IsNaN(audioStartBeat)
            && !double.IsNaN(audioEndBeat)
            && !double.IsInfinity(audioStartBeat)
            && !double.IsInfinity(audioEndBeat)
            && audioEndBeat > audioStartBeat;

        double audioStartX = boundsKnown ? (audioStartBeat - offset) * ppb : 0;
        double audioEndX = boundsKnown ? (audioEndBeat - offset) * ppb : totalWidth;
        double drawStartX = Math.Max(0, audioStartX);
        double drawEndX = Math.Min(totalWidth, audioEndX);

        PrepareEnvelopeCache(
            samples,
            totalWidth,
            audioStartBeat,
            audioEndBeat,
            offset,
            ppb,
            audioStartX,
            audioEndX,
            request.TimelineStructure);

        int visibleStartX = Math.Max(0, (int)visiblePixelStart);
        int visibleEndX = Math.Min(totalWidth, (int)Math.Ceiling(visiblePixelEnd));
        int bufferSize = Math.Max(1, (visibleEndX - visibleStartX) / 10);
        int renderStartX = Math.Max(0, visibleStartX - bufferSize);
        int renderEndX = Math.Min(totalWidth, visibleEndX + bufferSize);

        if (boundsKnown && drawStartX > renderStartX)
            DrawNoAudioStripes(context, new Rect(renderStartX, 0, drawStartX - renderStartX, bounds.Height));

        int waveStart = Math.Max(renderStartX, (int)drawStartX);
        int waveEnd = Math.Min(renderEndX, (int)drawEndX);
        if (waveEnd <= waveStart)
            return;

        double audioDurationSeconds = request.TimelineStructure is { Markers.Count: >= 2 }
            ? request.TimelineStructure.GetPlaybackSecondsAtBeat(audioEndBeat)
            : 0;
        (int firstTile, int lastTile) = GetEnvelopeTileRange(waveStart, waveEnd);
        for (int tileIndex = firstTile; tileIndex <= lastTile; tileIndex++)
        {
            EnvelopeTile tile = GetOrBuildEnvelopeTile(
                tileIndex,
                samples,
                totalWidth,
                offset,
                ppb,
                audioStartX,
                audioEndX,
                audioDurationSeconds,
                request.TimelineStructure);
            int tileRenderStart = Math.Max(waveStart, tile.StartX);
            int tileRenderEnd = Math.Min(waveEnd, tile.StartX + tile.Max.Length);

            for (int x = tileRenderStart; x < tileRenderEnd; x++)
            {
                int tileOffset = x - tile.StartX;
                float maxV = tile.Max[tileOffset];
                float minV = tile.Min[tileOffset];
                double topY = centerY - (maxV * centerY * 0.8);
                double botY = centerY - (minV * centerY * 0.8);
                double height = botY - topY;

                if (height < 0.5)
                    continue;

                Rect columnRect = new(x, topY, 1, height);
                context.FillRectangle(TimelineResources.WaveformFill, columnRect);
                context.DrawLine(TimelineResources.WaveformEdgePen, new Point(x, topY), new Point(x + 1, topY));
                context.DrawLine(TimelineResources.WaveformEdgePen, new Point(x, botY), new Point(x + 1, botY));
            }
        }

        if (boundsKnown && drawEndX < renderEndX)
            DrawNoAudioStripes(context, new Rect(drawEndX, 0, renderEndX - drawEndX, bounds.Height));
    }

    private void PrepareEnvelopeCache(
        float[] samples,
        int totalWidth,
        double audioStartBeat,
        double audioEndBeat,
        double offset,
        double ppb,
        double audioStartX,
        double audioEndX,
        TimelineStructureDocument? timelineStructure)
    {
        bool cacheStale = !ReferenceEquals(_envelopeCacheSamples, samples)
            || _envelopeCacheWidth != totalWidth
            || CacheValueChanged(_envelopeCacheAudioStartBeat, audioStartBeat)
            || CacheValueChanged(_envelopeCacheAudioEndBeat, audioEndBeat)
            || CacheValueChanged(_envelopeCacheBeatOffset, offset)
            || CacheValueChanged(_envelopeCachePpb, ppb)
            || !ReferenceEquals(_envelopeCacheTimelineStructure, timelineStructure);
        if (!cacheStale)
            return;

        _envelopeCacheWidth = totalWidth;
        _envelopeCacheSamples = samples;
        _envelopeCacheAudioStartBeat = audioStartBeat;
        _envelopeCacheAudioEndBeat = audioEndBeat;
        _envelopeCacheBeatOffset = offset;
        _envelopeCachePpb = ppb;
        _envelopeCacheTimelineStructure = timelineStructure;
        _envelopeTiles.Clear();
    }

    private EnvelopeTile GetOrBuildEnvelopeTile(
        int tileIndex,
        float[] samples,
        int totalWidth,
        double offset,
        double ppb,
        double audioStartX,
        double audioEndX,
        double audioDurationSeconds,
        TimelineStructureDocument? timelineStructure)
    {
        if (_envelopeTiles.TryGetValue(tileIndex, out EnvelopeTile? cached))
            return cached;

        int tileStart = tileIndex * EnvelopeTileWidth;
        int tileEnd = Math.Min(totalWidth, tileStart + EnvelopeTileWidth);
        float[] maximums = new float[Math.Max(0, tileEnd - tileStart)];
        float[] minimums = new float[maximums.Length];

        double audioPixelWidth = audioEndX - audioStartX;
        for (int x = tileStart; x < tileEnd; x++)
        {
            int startIndex;
            int endIndex;
            if (timelineStructure is { Markers.Count: >= 2 } && audioDurationSeconds > 0)
            {
                double beatStart = offset + (x / ppb);
                double beatEnd = offset + ((x + 1.0) / ppb);
                (startIndex, endIndex) = GetWaveformSampleRange(
                    timelineStructure,
                    beatStart,
                    beatEnd,
                    audioDurationSeconds,
                    samples.Length);
            }
            else
            {
                double t = audioPixelWidth > 0 ? (x - audioStartX) / audioPixelWidth : -1;
                if (t is < 0 or >= 1)
                    continue;

                startIndex = (int)(t * samples.Length);
                endIndex = (int)((t + (1.0 / audioPixelWidth)) * samples.Length);
                endIndex = Math.Min(samples.Length, Math.Max(endIndex, startIndex + 1));
            }

            if (startIndex >= samples.Length || endIndex <= 0 || endIndex <= startIndex)
                continue;

            float maxV = 0;
            float minV = 0;
            for (int i = startIndex; i < endIndex && i < samples.Length; i++)
            {
                float value = samples[i];
                if (value > maxV)
                    maxV = value;
                if (value < minV)
                    minV = value;
            }

            int tileOffset = x - tileStart;
            maximums[tileOffset] = maxV;
            minimums[tileOffset] = minV;
        }

        EnvelopeTile tile = new(tileStart, maximums, minimums);
        _envelopeTiles[tileIndex] = tile;
        return tile;
    }

    internal static (int FirstTile, int LastTile) GetEnvelopeTileRange(int startX, int endX) =>
        endX <= startX
            ? (0, -1)
            : (Math.Max(0, startX) / EnvelopeTileWidth, Math.Max(0, endX - 1) / EnvelopeTileWidth);

    private static bool CacheValueChanged(double cached, double value)
    {
        if (cached.Equals(value))
            return false;
        if (!double.IsFinite(cached) || !double.IsFinite(value))
            return true;
        return Math.Abs(cached - value) > 0.001;
    }

    private sealed record EnvelopeTile(int StartX, float[] Max, float[] Min);

    internal static (int Start, int End) GetWaveformSampleRange(
        TimelineStructureDocument timelineStructure,
        double beatStart,
        double beatEnd,
        double audioDurationSeconds,
        int sampleCount)
    {
        if (audioDurationSeconds <= 0 || sampleCount <= 0)
            return (0, 0);

        double startSeconds = timelineStructure.GetPlaybackSecondsAtBeat(beatStart);
        double endSeconds = timelineStructure.GetPlaybackSecondsAtBeat(beatEnd);
        int start = (int)Math.Floor(startSeconds / audioDurationSeconds * sampleCount);
        int end = (int)Math.Ceiling(endSeconds / audioDurationSeconds * sampleCount);
        start = Math.Clamp(start, 0, sampleCount);
        end = Math.Clamp(end, 0, sampleCount);
        if (end <= start && start < sampleCount)
            end = start + 1;
        return (start, end);
    }

    private void DrawLabelsAndTooltip(AudioBarRenderRequest request, double visiblePixelStart, double visiblePixelEnd)
    {
        IReadOnlyList<SectionSegment> sortedSections = request.SortedSections;
        if (sortedSections.Count == 0)
            return;

        DrawingContext context = request.Context;
        Rect bounds = request.Bounds;
        double ppb = request.PixelsPerBeat;
        double offset = request.BeatOffset;

        if (Math.Abs(_lastPixelsPerBeat - ppb) > 1e-9 || !_lastBounds.Equals(bounds.Size))
        {
            _sectionTextCache.Clear();
            _signatureTextCache.Clear();
            foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
            {
                _sectionTextCache[type] = new FormattedText(
                    type.ToString(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    TimelineResources.DefaultTypeface,
                    10,
                    Brushes.White);
            }

            _lastPixelsPerBeat = ppb;
            _lastBounds = bounds.Size;
        }

        foreach (SectionSegment section in sortedSections)
        {
            double x = (section.StartBeat - offset) * ppb;
            if (x < visiblePixelStart || x >= visiblePixelEnd)
                continue;

            context.DrawLine(TimelineResources.SectionBorderPen, new Point(x, 0), new Point(x, bounds.Height));
            if (!_sectionTextCache.TryGetValue(section.SectionType, out FormattedText? text))
                continue;

            Rect bgRect = new(x + 2, 2, text.Width + 4, text.Height + 2);
            context.FillRectangle(TimelineResources.SectionBgBrush, bgRect);
            context.DrawText(text, new Point(x + 4, 3));
            request.SectionLabelRects.Add((bgRect, section));
        }

        DrawSignatureLabels(request, visiblePixelStart, visiblePixelEnd);
        DrawTooltip(request);
    }

    private void DrawSignatureLabels(AudioBarRenderRequest request, double visiblePixelStart, double visiblePixelEnd)
    {
        DrawingContext context = request.Context;
        Rect bounds = request.Bounds;
        double ppb = request.PixelsPerBeat;
        double offset = request.BeatOffset;

        foreach (SignatureSegment signature in request.SortedSignatures)
        {
            double x = (signature.Marker - offset) * ppb;
            if (x < visiblePixelStart || x >= visiblePixelEnd)
                continue;

            if (!_signatureTextCache.TryGetValue(signature.Beats, out FormattedText? signatureText))
            {
                signatureText = new FormattedText(
                    $"{signature.Beats}/4",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    TimelineResources.DefaultTypeface,
                    10,
                    Brushes.White);
                _signatureTextCache[signature.Beats] = signatureText;
            }

            double textY = bounds.Height - signatureText.Height - 2;
            Rect bgRect = new(x + 2, textY - 2, signatureText.Width + 4, signatureText.Height + 2);
            context.FillRectangle(TimelineResources.SectionBgBrush, bgRect);
            context.DrawText(signatureText, new Point(x + 4, textY));
            request.SignatureLabelRects.Add((bgRect, signature));
        }
    }

    private static void DrawTooltip(AudioBarRenderRequest request)
    {
        if (request.HoveredSection == null || request.TooltipText == null || request.IsDragging || request.IsScrubbing)
            return;

        Rect bounds = request.Bounds;
        Rect tooltipBgRect = new(
            request.TooltipPosition.X,
            request.TooltipPosition.Y,
            request.TooltipText.Width + 8,
            request.TooltipText.Height + 4);

        if (tooltipBgRect.X + tooltipBgRect.Width > bounds.Width)
            tooltipBgRect = tooltipBgRect.WithX(bounds.Width - tooltipBgRect.Width - 4);
        if (tooltipBgRect.X < 0)
            tooltipBgRect = tooltipBgRect.WithX(4);
        if (tooltipBgRect.Y < 0)
            tooltipBgRect = tooltipBgRect.WithY(request.TooltipPosition.Y + 30);

        request.Context.FillRectangle(TooltipBackgroundBrush, tooltipBgRect);
        request.Context.DrawRectangle(TooltipBorderPen, tooltipBgRect);
        request.Context.DrawText(request.TooltipText, new Point(tooltipBgRect.X + 4, tooltipBgRect.Y + 2));
    }

    private static void DrawNoAudioStripes(DrawingContext context, Rect region)
    {
        RenderingHelpers.DrawDiagonalStripes(
            context,
            region,
            Color.FromArgb(80, 180, 0, 0),
            Color.FromArgb(100, 255, 255, 255));
    }

    private static void DrawMeasureBackgrounds(
        DrawingContext context,
        Rect bounds,
        double ppb,
        int offset,
        double maxBeat,
        double visibleStartBeat,
        double visibleEndBeat,
        double visiblePixelStart,
        double visiblePixelEnd,
        IReadOnlyList<SignatureSegment> sortedSigs,
        List<double> sectionStarts)
    {
        double rangeStart = Math.Max(offset, visibleStartBeat);
        double rangeEnd = Math.Min(offset + maxBeat, visibleEndBeat);
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
                double sEnd = i + 1 < sectionStarts.Count ? sectionStarts[i + 1] : rangeEnd;
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
                double groupEnd = pos + blockSize;
                bool partialSectionEnd = groupEnd > sEnd + 0.01;
                if (partialSectionEnd)
                    groupEnd = sEnd;

                double nextSignature = TimelineRenderHelper.GetNextSigChange(pos, sortedSigs);
                bool partialSignatureChange = false;
                if (!partialSectionEnd && nextSignature < groupEnd - 0.01)
                {
                    groupEnd = nextSignature;
                    partialSignatureChange = true;
                }

                bool isPartial = partialSignatureChange || (partialSectionEnd && !isLastSection);
                double xStart = (pos - offset) * ppb;
                double xEnd = (groupEnd - offset) * ppb;
                if (xEnd >= visiblePixelStart && xStart <= visiblePixelEnd)
                {
                    SolidColorBrush brush = isPartial
                        ? MeasureBrushError
                        : (sectionColor + groupInSection) % 2 == 0
                            ? TimelineResources.MeasureBrushA
                            : TimelineResources.MeasureBrushB;
                    double clippedStart = Math.Max(xStart, visiblePixelStart);
                    double clippedEnd = Math.Min(xEnd, visiblePixelEnd);
                    context.FillRectangle(brush, new Rect(clippedStart, 0, clippedEnd - clippedStart, bounds.Height));
                }

                groupInSection++;
                pos = groupEnd;
            }

            colorIndex += groupInSection;
        }
    }

    private static void DrawGridLines(
        DrawingContext context,
        Rect bounds,
        double ppb,
        int offset,
        double visibleStartBeat,
        double visibleEndBeat,
        double visiblePixelStart,
        double visiblePixelEnd,
        List<double> sectionStarts)
    {
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

    private static SolidColorBrush GetSectionBackgroundBrush(SongSectionType sectionType)
    {
        if (SectionBackgroundBrushes.TryGetValue(sectionType, out SolidColorBrush? brush))
            return brush;

        Color color = SectionColors.TryGetValue(sectionType, out Color c) ? c : Colors.Gray;
        brush = new SolidColorBrush(color, 0.3);
        SectionBackgroundBrushes[sectionType] = brush;
        return brush;
    }
}

internal sealed class AudioBarRenderRequest
{
    public required DrawingContext Context { get; init; }
    public required Rect Bounds { get; init; }
    public required double PixelsPerBeat { get; init; }
    public required int BeatOffset { get; init; }
    public required float[]? Samples { get; init; }
    public required double AudioStartBeat { get; init; }
    public required double AudioEndBeat { get; init; }
    public required TimelineStructureDocument? TimelineStructure { get; init; }
    public required double VisiblePixelStart { get; init; }
    public required double VisiblePixelEnd { get; init; }
    public required IReadOnlyList<SectionSegment> SortedSections { get; init; }
    public required IReadOnlyList<SignatureSegment> SortedSignatures { get; init; }
    public required List<double> SectionStarts { get; init; }
    public required List<(Rect rect, SectionSegment section)> SectionLabelRects { get; init; }
    public required List<(Rect rect, SignatureSegment sig)> SignatureLabelRects { get; init; }
    public required SectionSegment? HoveredSection { get; init; }
    public required FormattedText? TooltipText { get; init; }
    public required Point TooltipPosition { get; init; }
    public required bool IsDragging { get; init; }
    public required bool IsScrubbing { get; init; }
}