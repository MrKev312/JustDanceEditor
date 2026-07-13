using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using KevInc.Avalonia.Timeline;

using System;
using System.Collections.Generic;
using System.ComponentModel;

using TimelineRenderHelper = KevInc.Avalonia.Timeline.TimelineRenderHelper;
using TimelineResources = KevInc.Avalonia.Timeline.TimelineResources;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimeRulerControl : ThemedTimelineControl
{
    private const double ViewportRenderPadding = 64;
    private const double MinimumLabelSpacing = 48;
    private static readonly SolidColorBrush MeasureBrushError = new(Colors.Red, 0.08);
    private static readonly Comparison<SignatureSegment> CompareSignaturesByMarker =
        static (left, right) => left.Marker.CompareTo(right.Marker);

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

    public static readonly StyledProperty<IEnumerable<SectionSegment>> SectionsProperty =
        AvaloniaProperty.Register<TimeRulerControl, IEnumerable<SectionSegment>>(nameof(Sections));

    public IEnumerable<SectionSegment> Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    static TimeRulerControl()
    {
        AffectsRender<TimeRulerControl>(PixelsPerBeatProperty, BeatOffsetProperty, MaxBeatProperty, SignaturesProperty, SectionsProperty);
    }

    private TimelineEditorViewModel? _subscribedVm;
    private ScrollViewer? _parentScrollViewer;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;
    private readonly List<SignatureSegment> _sortedSignatureCache = [];
    private readonly List<double> _sectionStartCache = [];
    private readonly Dictionary<int, FormattedText> _beatLabelCache = [];
    private bool _signatureCacheDirty = true;
    private bool _sectionCacheDirty = true;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToViewModel();
        SubscribeToScrollViewer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromScrollViewer();
        UnsubscribeFromViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnsubscribeFromViewModel();
        SubscribeToViewModel();
    }

    private void SubscribeToViewModel()
    {
        if (DataContext is TimelineEditorViewModel vm && vm != _subscribedVm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnsubscribeFromViewModel()
    {
        _subscribedVm?.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedVm = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineEditorViewModel.TimelineStructure))
        {
            _signatureCacheDirty = true;
            _sectionCacheDirty = true;
            InvalidateVisual();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SignaturesProperty)
            _signatureCacheDirty = true;
        else if (change.Property == SectionsProperty)
            _sectionCacheDirty = true;
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
        long renderStart = TimelineRenderDiagnostics.Start();
        try
        {
            Rect bounds = Bounds;
            double ppb = PixelsPerBeat;
            int offset = BeatOffset;
            double max = MaxBeat;
            (double visiblePixelStart, double visiblePixelEnd) = GetVisiblePixelRange(bounds.Width);
            double visibleStartBeat = offset + (visiblePixelStart / Math.Max(1.0, ppb));
            double visibleEndBeat = offset + (visiblePixelEnd / Math.Max(1.0, ppb));

            // 0. Catch all pointer events by drawing a transparent background
            context.FillRectangle(Brushes.Transparent, bounds);

            // 1. Draw Alternating Measure Backgrounds
            IReadOnlyList<SignatureSegment> sortedSigs = GetSortedSignatures();
            List<double> sectionStarts = GetSectionStarts();

            {
                double rangeStart = Math.Max(offset, visibleStartBeat - 1);
                double rangeEnd = Math.Min(offset + max, visibleEndBeat + 1);

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
                        pos = gEnd;
                    }

                    colorIndex += groupInSection;
                }
            }

            // 2. Draw Ticks and Labels
            Pen mainPen = TimelineResources.MainPen;
            Pen tickPen = TimelineResources.TickPen;
            IBrush labelBrush = TimelineResources.LabelBrush;

            context.DrawLine(mainPen, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));

            int firstBeat = Math.Max(0, (int)Math.Floor(visiblePixelStart / Math.Max(1.0, ppb)) - 1);
            int lastBeat = Math.Min((int)Math.Ceiling(max), (int)Math.Ceiling(visiblePixelEnd / Math.Max(1.0, ppb)) + 1);
            int labelBeatInterval = GetLabelBeatInterval(ppb);

            for (int i = firstBeat; i <= lastBeat; i++)
            {
                double x = i * ppb;
                if (x < visiblePixelStart)
                    continue;
                if (x > visiblePixelEnd)
                    break;

                int beat = i + offset;
                bool shouldDrawLabel = ShouldDrawBeatLabel(beat, labelBeatInterval);
                bool isMajor = TimelineRenderHelper.IsBaseGroupBeat(beat, sectionStarts);
                double tickHeight = isMajor || shouldDrawLabel ? 12 : 6;

                context.DrawLine(tickPen, new Point(x, bounds.Height), new Point(x, bounds.Height - tickHeight));

                if (shouldDrawLabel)
                {
                    FormattedText text = GetBeatLabelText(beat, labelBrush);
                    context.DrawText(text, new Point(x + 3, bounds.Height - tickHeight - 12));
                }
            }
        }
        finally
        {
            TimelineRenderDiagnostics.RecordDuration("ruler.render", renderStart);
        }
    }

    internal static int GetLabelBeatInterval(double pixelsPerBeat)
    {
        const double baseGroupBeats = 4;
        double pixelsPerGroup = Math.Max(1, pixelsPerBeat * baseGroupBeats);
        int groupInterval = Math.Max(1, (int)Math.Ceiling(MinimumLabelSpacing / pixelsPerGroup));
        return groupInterval * (int)baseGroupBeats;
    }

    internal static bool ShouldDrawBeatLabel(int beat, int labelBeatInterval) =>
        beat % Math.Max(1, labelBeatInterval) == 0;

    private FormattedText GetBeatLabelText(int beat, IBrush labelBrush)
    {
        if (_beatLabelCache.TryGetValue(beat, out FormattedText? text))
            return text;

        text = new FormattedText(
            beat.ToString(),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            TimelineResources.DefaultTypeface,
            10,
            labelBrush);
        _beatLabelCache[beat] = text;
        return text;
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

    private void SubscribeToScrollViewer()
    {
        _parentScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_parentScrollViewer == null)
            return;

        _scrollChangedHandler = (_, _) => InvalidateVisual();
        _parentScrollViewer.ScrollChanged += _scrollChangedHandler;
    }

    private void UnsubscribeFromScrollViewer()
    {
        if (_parentScrollViewer != null && _scrollChangedHandler != null)
            _parentScrollViewer.ScrollChanged -= _scrollChangedHandler;

        _parentScrollViewer = null;
        _scrollChangedHandler = null;
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
}
