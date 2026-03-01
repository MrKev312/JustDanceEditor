using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.Views.Timeline;

public class AudioBarControl : Control
{
    public static readonly StyledProperty<float[]> SamplesProperty =
        AvaloniaProperty.Register<AudioBarControl, float[]>(nameof(Samples));

    public float[] Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(PixelsPerBeat), 50.0);

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public static readonly StyledProperty<int> BeatOffsetProperty =
        AvaloniaProperty.Register<AudioBarControl, int>(nameof(BeatOffset), 0);

    public int BeatOffset
    {
        get => GetValue(BeatOffsetProperty);
        set => SetValue(BeatOffsetProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<SectionSegment>> SectionsProperty =
        AvaloniaProperty.Register<AudioBarControl, IEnumerable<SectionSegment>>(nameof(Sections));

    public IEnumerable<SectionSegment> Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<SignatureSegment>> SignaturesProperty =
        AvaloniaProperty.Register<AudioBarControl, IEnumerable<SignatureSegment>>(nameof(Signatures));

    public IEnumerable<SignatureSegment> Signatures
    {
        get => GetValue(SignaturesProperty);
        set => SetValue(SignaturesProperty, value);
    }

    private static readonly Dictionary<SongSectionType, Color> _sectionColors = [];

    // Cached/render resources moved to TimelineResources
    // Cache FormattedText per section type to avoid allocations in render loop
    private readonly Dictionary<SongSectionType, FormattedText> _sectionTextCache = [];
    private double _lastPixelsPerBeat = -1;
    private Size _lastBounds = default;

    // Waveform envelope cache: one min/max pair per pixel column
    private float[]? _envelopeMax;
    private float[]? _envelopeMin;
    private int _envelopeCacheWidth;
    private float[]? _envelopeCacheSamples;

    // scrubbing state
    private bool _isScrubbing = false;
    private ScrollViewer? _parentScrollViewer;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;

    static AudioBarControl()
    {
        AffectsRender<AudioBarControl>(SamplesProperty, PixelsPerBeatProperty, SectionsProperty, BeatOffsetProperty, SignaturesProperty);

        // Pre-cache section colors
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
            ColorAttribute? attr = field?.GetCustomAttribute<ColorAttribute>();
            if (attr != null)
            {
                _sectionColors[type] = Color.FromRgb(attr.R, attr.G, attr.B);
            }
            else
            {
                _sectionColors[type] = Colors.Gray;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // Find parent ScrollViewer and attach scroll listener for viewport changes
        _parentScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_parentScrollViewer != null)
        {
            _scrollChangedHandler = (s, ev) => InvalidateVisual();
            _parentScrollViewer.ScrollChanged += _scrollChangedHandler;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_parentScrollViewer != null && _scrollChangedHandler != null)
        {
            _parentScrollViewer.ScrollChanged -= _scrollChangedHandler;
            _parentScrollViewer = null;
            _scrollChangedHandler = null;
        }
        
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPoint point = e.GetCurrentPoint(this);
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // Double-click to jump to section (existing behavior)
        if (e.ClickCount == 2 && Sections != null)
        {
            double clickedBeat = (point.Position.X / ppb) + offset;

            // Find the section that contains or starts at this beat
            SectionSegment? section = Sections.OrderByDescending(s => s.StartBeat)
                                 .FirstOrDefault(s => s.StartBeat <= clickedBeat);

            if (section != null && DataContext is TimelineEditorViewModel vm)
            {
                vm.Playback.SeekToBeat(section.StartBeat);
                e.Handled = true;
                return;
            }
        }

        // Start scrubbing on left button
        if (point.Properties.IsLeftButtonPressed && DataContext is TimelineEditorViewModel vm2)
        {
            _isScrubbing = true;
            // capture pointer
            try
            {
                e.Pointer.Capture(this);
            }
            catch { }

            SeekAtPointer(point.Position.X, vm2);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isScrubbing)
            return;
        PointerPoint point = e.GetCurrentPoint(this);
        if (DataContext is TimelineEditorViewModel vm)
        {
            SeekAtPointer(point.Position.X, vm);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isScrubbing)
            return;
        _isScrubbing = false;
        try
        {
            e.Pointer.Capture(null);
        }
        catch { }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isScrubbing = false;
    }

    private static void SeekAtPointer(double x, TimelineEditorViewModel vm)
    {
        // Convert local X to beat
        double beat = (x / vm.PixelsPerBeat) + vm.BeatOffset;

        // Apply centralized snapping logic
        beat = SnappingService.FindSnapBeat(beat, vm);

        vm.Playback.SeekToBeat(beat);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // 1. Draw Section Backgrounds
        if (Sections != null)
        {
            List<SectionSegment> sortedSections = [.. Sections.OrderBy(s => s.StartBeat)];
            for (int i = 0; i < sortedSections.Count; i++)
            {
                SectionSegment section = sortedSections[i];
                Color color = _sectionColors.TryGetValue(section.SectionType, out Color c) ? c : Colors.Gray;

                double startX = (section.StartBeat - offset) * ppb;
                double endX = bounds.Width;

                if (i + 1 < sortedSections.Count)
                {
                    endX = (sortedSections[i + 1].StartBeat - offset) * ppb;
                }

                if (startX < bounds.Width && endX > 0)
                {
                    SolidColorBrush sectionBrush = new(color, 0.3);
                    Rect rect = new(Math.Max(0, startX), 0, Math.Min(bounds.Width, endX) - Math.Max(0, startX), bounds.Height);
                    context.FillRectangle(sectionBrush, rect);
                }
            }
        }

        // 1.5. Draw Measure Backgrounds (alternating pattern)
        double maxBeat = bounds.Width / Math.Max(1.0, ppb);
        DrawMeasureBackgrounds(context, bounds, ppb, (int)offset, maxBeat);

        // 1.75. Draw Grid Lines
        double visibleStartBeat = offset - 1;
        double visibleEndBeat = offset + (bounds.Width / Math.Max(1.0, ppb)) + 1;
        DrawGridLines(context, bounds, ppb, (int)offset, visibleStartBeat, visibleEndBeat);

        // 2. Draw Waveform
        if (Samples != null && Samples.Length > 0)
        {
            int totalWidth = (int)bounds.Width;
            double centerY = bounds.Height / 2;

            // Rebuild envelope cache when samples or control width change
            if (_envelopeCacheSamples != Samples || _envelopeCacheWidth != totalWidth)
            {
                _envelopeCacheWidth = totalWidth;
                _envelopeCacheSamples = Samples;
                _envelopeMax = new float[totalWidth];
                _envelopeMin = new float[totalWidth];

                for (int x = 0; x < totalWidth; x++)
                {
                    int startIdx = (int)((double)x / totalWidth * Samples.Length);
                    int endIdx = (int)((double)(x + 1) / totalWidth * Samples.Length);
                    if (endIdx > Samples.Length) endIdx = Samples.Length;
                    if (startIdx >= endIdx) endIdx = startIdx + 1;

                    float maxV = 0, minV = 0;
                    for (int i = startIdx; i < endIdx && i < Samples.Length; i++)
                    {
                        float val = Samples[i];
                        if (val > maxV) maxV = val;
                        if (val < minV) minV = val;
                    }
                    _envelopeMax[x] = maxV;
                    _envelopeMin[x] = minV;
                }
            }

            // Find the visible pixel range from the parent ScrollViewer
            int visibleStartX = 0;
            int visibleEndX = totalWidth;

            ScrollViewer? sv = this.FindAncestorOfType<ScrollViewer>();
            if (sv != null)
            {
                visibleStartX = Math.Max(0, (int)sv.Offset.X);
                visibleEndX = Math.Min(totalWidth, (int)(sv.Offset.X + sv.Viewport.Width));
            }

            // Add 10% buffer on both sides for smooth scrolling
            int bufferSize = Math.Max(1, (visibleEndX - visibleStartX) / 10);
            int renderStartX = Math.Max(0, visibleStartX - bufferSize);
            int renderEndX = Math.Min(totalWidth, visibleEndX + bufferSize);

            // Draw only visible + buffered columns using cached envelope
            for (int x = renderStartX; x < renderEndX; x++)
            {
                float maxV = _envelopeMax![x];
                float minV = _envelopeMin![x];

                double topH = maxV * centerY * 0.8;
                double botH = minV * centerY * 0.8;

                double topY = centerY - topH;
                double botY = centerY - botH;
                double height = botY - topY;

                if (height < 0.5)
                    continue;

                // Semi-transparent fill
                Rect columnRect = new(x, topY, 1, height);
                context.FillRectangle(TimelineResources.WaveformFill, columnRect);

                // White edge pixels at top and bottom
                context.DrawLine(TimelineResources.WaveformEdgePen, new Point(x, topY), new Point(x + 1, topY));
                context.DrawLine(TimelineResources.WaveformEdgePen, new Point(x, botY), new Point(x + 1, botY));
            }
        }

        // 3. Draw Section Labels
        if (Sections != null)
        {
            List<SectionSegment> sortedSections = [.. Sections.OrderBy(s => s.StartBeat)];

            // Rebuild text cache only when size or pixels-per-beat changes
            if (Math.Abs(_lastPixelsPerBeat - ppb) > 1e-9 || !_lastBounds.Equals(bounds.Size))
            {
                _sectionTextCache.Clear();
                foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
                {
                    FormattedText ft = new(
                        type.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        TimelineResources.DefaultTypeface,
                        10,
                        Brushes.White);
                    _sectionTextCache[type] = ft;
                }

                _lastPixelsPerBeat = ppb;
                _lastBounds = bounds.Size;
            }

            foreach (SectionSegment? section in sortedSections)
            {
                double x = (section.StartBeat - offset) * ppb;
                if (x >= 0 && x < bounds.Width)
                {
                    // Draw vertical line
                    context.DrawLine(TimelineResources.SectionBorderPen, new Point(x, 0), new Point(x, bounds.Height));

                    if (_sectionTextCache.TryGetValue(section.SectionType, out FormattedText? text))
                    {
                        Rect bgRect = new(x + 2, 2, text.Width + 4, text.Height + 2);
                        context.FillRectangle(TimelineResources.SectionBgBrush, bgRect);
                        context.DrawText(text, new Point(x + 4, 3));
                    }
                }
            }
        }
    }

    private void DrawMeasureBackgrounds(DrawingContext context, Rect bounds, double ppb, int offset, double maxBeat)
    {
        if (Signatures != null)
        {
            List<SignatureSegment> sortedSig = [.. Signatures.OrderBy(s => s.Marker)];
            if (sortedSig.Count == 0)
            {
                DrawMeasures(context, 0, maxBeat, 4, ppb, offset, bounds.Height, bounds.Width);
            }
            else
            {
                for (int i = 0; i < sortedSig.Count; i++)
                {
                    double startBeat = sortedSig[i].Marker - offset;
                    double endBeat = (i + 1 < sortedSig.Count) ? sortedSig[i + 1].Marker - offset : maxBeat;
                    int beatsPerMeasure = sortedSig[i].Beats;

                    DrawMeasures(context, startBeat, endBeat, beatsPerMeasure, ppb, offset, bounds.Height, bounds.Width);
                }
            }
        }
        else
        {
            DrawMeasures(context, 0, maxBeat, 4, ppb, offset, bounds.Height, bounds.Width);
        }
    }

    private static void DrawMeasures(DrawingContext context, double startBeat, double endBeat, int bpm, double ppb, int offset, double height, double boundsWidth)
    {
        double actualStartBeat = startBeat + offset;
        double actualEndBeat = endBeat + offset;

        SolidColorBrush brushA = new(Colors.White, 0.05);
        SolidColorBrush brushB = new(Colors.White, 0.02);

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

            if (xStart > boundsWidth)
                break;

            SolidColorBrush brush = (measureIndex % 2 == 0) ? brushA : brushB;
            context.FillRectangle(brush, new Rect(xStart, 0, xEnd - xStart, height));

            measureIndex++;
        }
    }

    private void DrawGridLines(DrawingContext context, Rect bounds, double ppb, int offset, double visibleStartBeat, double visibleEndBeat)
    {
        // Draw measure and beat grid lines
        for (int beat = (int)visibleStartBeat; beat <= (int)visibleEndBeat; beat++)
        {
            double x = (beat - offset) * ppb;
            if (x < 0 || x > bounds.Width)
                continue;

            // Measure lines every 4 beats (opaque)
            if (beat % 4 == 0)
            {
                context.DrawLine(TimelineResources.MeasureGridPen, new Point(x, 0), new Point(x, bounds.Height));
            }
            // Beat lines (semi-transparent)
            else if (beat % 1 == 0)
            {
                context.DrawLine(TimelineResources.BeatGridPen, new Point(x, 0), new Point(x, bounds.Height));
            }
        }
    }
}