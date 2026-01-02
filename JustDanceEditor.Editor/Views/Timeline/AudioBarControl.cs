using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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

    private static readonly Dictionary<SongSectionType, Color> _sectionColors = [];

    // Cached/render resources moved to TimelineResources
    // Cache FormattedText per section type to avoid allocations in render loop
    private readonly Dictionary<SongSectionType, FormattedText> _sectionTextCache = [];
    private double _lastPixelsPerBeat = -1;
    private Size _lastBounds = default;

    // scrubbing state
    private bool _isScrubbing = false;

    static AudioBarControl()
    {
        AffectsRender<AudioBarControl>(SamplesProperty, PixelsPerBeatProperty, SectionsProperty, BeatOffsetProperty);

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

        // 2. Draw Waveform
        if (Samples != null && Samples.Length > 0)
        {
            double totalWidth = bounds.Width;
            double centerY = bounds.Height / 2;

            int step = Math.Max(1, Samples.Length / (int)Math.Max(1, totalWidth));
            for (int x = 0; x < (int)totalWidth; x++)
            {
                int sampleIdx = (int)((double)x / totalWidth * Samples.Length);
                if (sampleIdx >= Samples.Length)
                    break;

                float val = Samples[sampleIdx];
                double h = val * centerY * 0.8;
                context.DrawLine(TimelineResources.WaveformPen, new Point(x, centerY - h), new Point(x, centerY + h));
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
}