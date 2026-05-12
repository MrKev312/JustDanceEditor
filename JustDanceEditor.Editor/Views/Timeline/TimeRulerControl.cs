using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using TimelineRenderHelper = KevInc.Avalonia.Timeline.TimelineRenderHelper;
using TimelineResources = KevInc.Avalonia.Timeline.TimelineResources;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimeRulerControl : Control
{
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
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
            InvalidateVisual();
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
        Rect bounds = Bounds;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;
        double max = MaxBeat;

        // 0. Catch all pointer events by drawing a transparent background
        context.FillRectangle(Brushes.Transparent, bounds);

        // 1. Draw Alternating Measure Backgrounds
        List<SignatureSegment> sortedSigs = Signatures?.OrderBy(s => s.Marker).ToList() ?? [];
        List<double> sectionStarts = Sections != null
            ? [.. Sections.OrderBy(s => s.StartBeat).Select(s => (double)s.StartBeat)]
            : [];

        {
            SolidColorBrush brushA = new(Colors.White, 0.05);
            SolidColorBrush brushB = new(Colors.White, 0.02);
            SolidColorBrush brushErr = new(Colors.Red, 0.08);

            double rangeStart = offset;
            double rangeEnd = offset + max;

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

        // 2. Draw Ticks and Labels
        Pen mainPen = TimelineResources.MainPen;
        Pen tickPen = TimelineResources.TickPen;
        IImmutableSolidColorBrush labelBrush = (IImmutableSolidColorBrush)TimelineResources.LabelBrush;

        context.DrawLine(mainPen, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));

        for (int i = 0; i <= max; i++)
        {
            double x = i * ppb;
            if (x < 0)
                continue;
            if (x > bounds.Width)
                break;

            bool isMajor = TimelineRenderHelper.IsBaseGroupBeat(i + offset, sectionStarts);
            double tickHeight = isMajor ? 12 : 6;

            context.DrawLine(tickPen, new Point(x, bounds.Height), new Point(x, bounds.Height - tickHeight));

            if (isMajor)
            {
                FormattedText text = new(
                    (i + offset).ToString(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    TimelineResources.DefaultTypeface,
                    10,
                    labelBrush);

                context.DrawText(text, new Point(x + 3, bounds.Height - tickHeight - 12));
            }
        }
    }
}
