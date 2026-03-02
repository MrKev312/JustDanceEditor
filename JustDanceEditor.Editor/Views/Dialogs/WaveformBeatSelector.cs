using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Dialogs;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Dialogs;

/// <summary>
/// A simple waveform control used in the New Song dialog for selecting the zero-beat position.
/// Displays the full waveform, a click marker for the zero beat, beat grid lines based on BPM,
/// alternating light/dark measure backgrounds, an integrated horizontal scrollbar, and a playhead.
/// Click/drag freely positions the zero-beat marker. Ctrl+click sets the playhead position.
/// </summary>
public class WaveformBeatSelector : Control
{
    private const double ScrollBarHeight = 14;
    private bool _scrollbarDragging;
    private double _scrollbarDragStartX;
    private double _scrollbarDragStartViewStart;

    public static readonly StyledProperty<float[]> SamplesProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, float[]>(nameof(Samples), []);

    public float[] Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public static readonly StyledProperty<double> AudioDurationProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, double>(nameof(AudioDuration), 1.0);

    public double AudioDuration
    {
        get => GetValue(AudioDurationProperty);
        set => SetValue(AudioDurationProperty, value);
    }

    public static readonly StyledProperty<double> ZeroBeatTimeProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, double>(nameof(ZeroBeatTime), 0.0);

    public double ZeroBeatTime
    {
        get => GetValue(ZeroBeatTimeProperty);
        set => SetValue(ZeroBeatTimeProperty, value);
    }

    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, double>(nameof(Bpm), 120.0);

    public double Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public static readonly StyledProperty<int> BeatsPerMeasureProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, int>(nameof(BeatsPerMeasure), 4);

    public int BeatsPerMeasure
    {
        get => GetValue(BeatsPerMeasureProperty);
        set => SetValue(BeatsPerMeasureProperty, value);
    }

    public static readonly StyledProperty<double> ViewStartProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, double>(nameof(ViewStart), 0.0);

    /// <summary>Start of the visible range in seconds.</summary>
    public double ViewStart
    {
        get => GetValue(ViewStartProperty);
        set => SetValue(ViewStartProperty, value);
    }

    public static readonly StyledProperty<double> ViewEndProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, double>(nameof(ViewEnd), 0.0);

    /// <summary>End of the visible range in seconds. When 0, defaults to AudioDuration.</summary>
    public double ViewEnd
    {
        get => GetValue(ViewEndProperty);
        set => SetValue(ViewEndProperty, value);
    }

    public static readonly StyledProperty<double> PlayheadTimeProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, double>(nameof(PlayheadTime), -1.0);

    /// <summary>Current playback position in seconds. Negative means hidden.</summary>
    public double PlayheadTime
    {
        get => GetValue(PlayheadTimeProperty);
        set => SetValue(PlayheadTimeProperty, value);
    }

    public static readonly StyledProperty<int> StartBeatProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, int>(nameof(StartBeat), 0);

    /// <summary>First beat of the padded song range (typically negative).</summary>
    public int StartBeat
    {
        get => GetValue(StartBeatProperty);
        set => SetValue(StartBeatProperty, value);
    }

    public static readonly StyledProperty<int> EndBeatProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, int>(nameof(EndBeat), 0);

    /// <summary>Last beat of the padded song range (positive).</summary>
    public int EndBeat
    {
        get => GetValue(EndBeatProperty);
        set => SetValue(EndBeatProperty, value);
    }

    public static readonly StyledProperty<ObservableCollection<SectionEntry>?> SectionsProperty =
        AvaloniaProperty.Register<WaveformBeatSelector, ObservableCollection<SectionEntry>?>(nameof(Sections));

    /// <summary>Section boundaries used for measure alternation pattern resets.</summary>
    public ObservableCollection<SectionEntry>? Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    private static readonly Pen _waveformPen = new(new SolidColorBrush(Colors.DarkGray), 1);
    private static readonly SolidColorBrush _waveformFill = new(Colors.LightGray, 0.4);
    private static readonly Pen _zeroBeatPen = new(new SolidColorBrush(Colors.Red), 2);
    private static readonly Pen _beatPen = new(new SolidColorBrush(Colors.White, 0.3), 1);
    private static readonly Pen _measurePen = new(new SolidColorBrush(Colors.White, 0.5), 1);
    private static readonly Pen _playheadPen = new(new SolidColorBrush(Colors.Lime), 2);
    private static readonly Pen _startBeatPen = new(new SolidColorBrush(Colors.Cyan), 2) { DashStyle = DashStyle.Dash };
    private static readonly Pen _endBeatPen = new(new SolidColorBrush(Colors.Orange), 2) { DashStyle = DashStyle.Dash };
    private static readonly SolidColorBrush _measureEvenBrush = new(Color.FromArgb(18, 255, 255, 255));
    private static readonly SolidColorBrush _measureOddBrush = new(Color.FromArgb(8, 255, 255, 255));
    private static readonly SolidColorBrush _measureErrorBrush = new(Color.FromArgb(25, 255, 60, 60));
    private static readonly SolidColorBrush _scrollTrackBrush = new(Color.FromRgb(20, 20, 20));
    private static readonly SolidColorBrush _scrollThumbBrush = new(Color.FromRgb(80, 80, 80));
    private static readonly SolidColorBrush _scrollThumbHoverBrush = new(Color.FromRgb(110, 110, 110));

    static WaveformBeatSelector()
    {
        AffectsRender<WaveformBeatSelector>(SamplesProperty, AudioDurationProperty, ZeroBeatTimeProperty, BpmProperty, BeatsPerMeasureProperty, ViewStartProperty, ViewEndProperty, PlayheadTimeProperty, StartBeatProperty, EndBeatProperty, SectionsProperty);
    }

    /// <summary>Height available for the waveform area (excludes scrollbar).</summary>
    private double WaveformHeight => Math.Max(0, Bounds.Height - ScrollBarHeight);

    private bool IsInScrollbar(double y) => y >= WaveformHeight;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint pt = e.GetCurrentPoint(this);

        if (pt.Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);

            if (IsInScrollbar(pt.Position.Y))
            {
                // Start scrollbar drag
                _scrollbarDragging = true;
                _scrollbarDragStartX = pt.Position.X;
                _scrollbarDragStartViewStart = ViewStart;
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+click sets playhead position
                SetPlayheadFromPixel(pt.Position.X);
            }
            else
            {
                UpdateZeroBeat(pt.Position.X);
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this)
            return;

        PointerPoint pt = e.GetCurrentPoint(this);

        if (_scrollbarDragging)
        {
            double dx = pt.Position.X - _scrollbarDragStartX;
            double duration = AudioDuration;
            if (duration <= 0 || Bounds.Width <= 0)
                return;

            double visibleDuration = GetVisibleEnd() - ViewStart;
            double timeDelta = dx / Bounds.Width * duration;
            double newStart = Math.Clamp(_scrollbarDragStartViewStart + timeDelta, 0, duration - visibleDuration);
            ViewStart = newStart;
            ViewEnd = newStart + visibleDuration;
        }
        else if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            UpdateZeroBeat(pt.Position.X);
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
        {
            _scrollbarDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        double visibleStart = ViewStart;
        double visibleEnd = GetVisibleEnd();
        double visibleDuration = visibleEnd - visibleStart;

        if (visibleDuration <= 0)
            return;

        // Get the mouse position as a fraction of the control width
        double mouseX = e.GetPosition(this).X;
        double fraction = mouseX / Bounds.Width;
        double mouseTime = visibleStart + (fraction * visibleDuration);

        // Zoom factor
        double zoomFactor = e.Delta.Y > 0 ? 0.8 : 1.25;
        double newDuration = Math.Clamp(visibleDuration * zoomFactor, 0.1, AudioDuration);

        // Recalculate start/end to keep the mouse position anchored
        double newStart = mouseTime - (fraction * newDuration);
        double newEnd = newStart + newDuration;

        // Clamp to audio bounds
        if (newStart < 0)
        {
            newStart = 0;
            newEnd = newDuration;
        }

        if (newEnd > AudioDuration)
        {
            newEnd = AudioDuration;
            newStart = Math.Max(0, AudioDuration - newDuration);
        }

        ViewStart = newStart;
        ViewEnd = newEnd;

        e.Handled = true;
    }

    private double GetVisibleEnd() => ViewEnd > 0 ? ViewEnd : AudioDuration;

    private void UpdateZeroBeat(double x)
    {
        double visibleStart = ViewStart;
        double visibleEnd = GetVisibleEnd();
        double visibleDuration = visibleEnd - visibleStart;
        if (visibleDuration <= 0 || Bounds.Width <= 0)
            return;

        double fraction = x / Bounds.Width;
        double time = visibleStart + (fraction * visibleDuration);

        ZeroBeatTime = Math.Clamp(time, 0, AudioDuration);
    }

    private void SetPlayheadFromPixel(double x)
    {
        double visibleStart = ViewStart;
        double visibleEnd = GetVisibleEnd();
        double visibleDuration = visibleEnd - visibleStart;
        if (visibleDuration <= 0 || Bounds.Width <= 0)
            return;

        double time = visibleStart + (x / Bounds.Width * visibleDuration);
        time = Math.Clamp(time, 0, AudioDuration);

        if (DataContext is ISongEditorViewModel vm)
            vm.SeekTo(time);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double width = bounds.Width;
        double totalHeight = bounds.Height;
        double height = WaveformHeight;

        // Full background
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), new Rect(bounds.Size));

        float[] samples = Samples;
        double duration = AudioDuration;
        if (samples == null || samples.Length == 0 || duration <= 0 || width < 2)
        {
            DrawScrollbar(context, width, totalHeight, height, duration);
            return;
        }

        double visibleStart = ViewStart;
        double visibleEnd = GetVisibleEnd();
        double visibleDuration = visibleEnd - visibleStart;
        if (visibleDuration <= 0)
            return;

        double centerY = height / 2;
        int totalSamples = samples.Length;

        // Draw alternating measure backgrounds
        List<double> sectionBeats = Sections?.OrderBy(s => s.StartBeat).Select(s => (double)s.StartBeat).ToList() ?? [];
        WaveformRenderHelper.DrawMeasureBackgrounds(context, width, height, visibleStart, visibleDuration, ZeroBeatTime, Bpm, BeatsPerMeasure, sectionBeats, _measureEvenBrush, _measureOddBrush, _measureErrorBrush);

        // Draw waveform envelope for visible region
        int pixelWidth = (int)width;
        for (int px = 0; px < pixelWidth; px++)
        {
            double t0 = visibleStart + (px / width * visibleDuration);
            double t1 = visibleStart + ((px + 1) / width * visibleDuration);

            int s0 = (int)(t0 / duration * totalSamples);
            int s1 = (int)(t1 / duration * totalSamples);
            s0 = Math.Clamp(s0, 0, totalSamples - 1);
            s1 = Math.Clamp(s1, s0 + 1, totalSamples);

            float maxV = 0, minV = 0;
            for (int i = s0; i < s1 && i < totalSamples; i++)
            {
                float v = samples[i];
                if (v > maxV)
                    maxV = v;
                if (v < minV)
                    minV = v;
            }

            double yTop = centerY - (maxV * centerY);
            double yBottom = centerY - (minV * centerY);

            if (yBottom - yTop > 1)
                context.FillRectangle(_waveformFill, new Rect(px, yTop, 1, yBottom - yTop));
            context.DrawLine(_waveformPen, new Point(px, yTop), new Point(px, yBottom));
        }

        // Draw beat grid lines from the zero beat position
        double bpm = Bpm;
        int bpMeasure = BeatsPerMeasure;
        if (bpm > 0)
        {
            double beatInterval = 60.0 / bpm;
            double zeroBeat = ZeroBeatTime;
            double firstBeatTime = zeroBeat - (Math.Ceiling((zeroBeat - visibleStart) / beatInterval) * beatInterval);

            int beatIndex = (int)Math.Round((firstBeatTime - zeroBeat) / beatInterval);
            for (double t = firstBeatTime; t <= visibleEnd; t += beatInterval)
            {
                if (t >= visibleStart)
                {
                    double x = (t - visibleStart) / visibleDuration * width;
                    bool isMeasure = bpMeasure > 0
                        && WaveformRenderHelper.IsMeasureBeat(beatIndex, bpMeasure, sectionBeats);
                    Pen pen = isMeasure ? _measurePen : _beatPen;
                    context.DrawLine(pen, new Point(x, 0), new Point(x, height));
                }

                beatIndex++;
            }
        }

        // Draw zero beat marker (red line)
        {
            double zeroBeatX = (ZeroBeatTime - visibleStart) / visibleDuration * width;
            if (zeroBeatX >= 0 && zeroBeatX <= width)
            {
                context.DrawLine(_zeroBeatPen, new Point(zeroBeatX, 0), new Point(zeroBeatX, height));

                FormattedText label = new(
                    "Beat 0",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                    11,
                    Brushes.Red);
                context.DrawText(label, new Point(zeroBeatX + 3, 2));
            }
        }

        // Draw start/end beat boundary lines
        if (Bpm > 0)
        {
            double beatDuration = 60.0 / Bpm;

            if (StartBeat != 0)
            {
                double startTime = ZeroBeatTime + (StartBeat * beatDuration);
                double sx = (startTime - visibleStart) / visibleDuration * width;
                if (sx >= 0 && sx <= width)
                {
                    context.DrawLine(_startBeatPen, new Point(sx, 0), new Point(sx, height));
                    FormattedText startLabel = new(
                        $"Start ({StartBeat})",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                        10,
                        Brushes.Cyan);
                    context.DrawText(startLabel, new Point(sx + 3, height - 16));
                }
            }

            if (EndBeat != 0)
            {
                double endTime = ZeroBeatTime + (EndBeat * beatDuration);
                double ex = (endTime - visibleStart) / visibleDuration * width;
                if (ex >= 0 && ex <= width)
                {
                    context.DrawLine(_endBeatPen, new Point(ex, 0), new Point(ex, height));
                    FormattedText endLabel = new(
                        $"End ({EndBeat})",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                        10,
                        Brushes.Orange);
                    context.DrawText(endLabel, new Point(ex + 3, height - 16));
                }
            }
        }

        // Draw playhead (green line) if active
        if (PlayheadTime >= 0)
        {
            double playheadX = (PlayheadTime - visibleStart) / visibleDuration * width;
            if (playheadX >= 0 && playheadX <= width)
            {
                context.DrawLine(_playheadPen, new Point(playheadX, 0), new Point(playheadX, height));
            }
        }

        // Draw scrollbar
        DrawScrollbar(context, width, totalHeight, height, duration);
    }

    private void DrawScrollbar(DrawingContext context, double width, double totalHeight, double waveformHeight, double duration)
    {
        WaveformRenderHelper.DrawScrollbar(context, width, totalHeight, waveformHeight, ScrollBarHeight, duration, ViewStart, GetVisibleEnd(), _scrollTrackBrush, _scrollbarDragging ? _scrollThumbHoverBrush : _scrollThumbBrush);
    }
}
