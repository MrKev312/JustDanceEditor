using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.Views.Dialogs;

/// <summary>
/// Waveform control for painting song sections. Extends the basic waveform display
/// with colored section regions and interactive section management.
/// <list type="bullet">
///   <item>Left-click near a section boundary: select it (then drag to move).</item>
///   <item>Left-click on empty area: places a new section at the nearest beat with the current PaintType.</item>
///   <item>Right-click: removes the section boundary at the nearest beat.</item>
///   <item>Ctrl+click: sets the playhead position for preview.</item>
/// </list>
/// When a section is selected, changing the PaintType combo updates that section's type.
/// Includes alternating measure backgrounds, a horizontal scrollbar, and a playhead.
/// </summary>
public class SectionWaveformEditor : Control
{
    private const double ScrollBarHeight = 14;
    private const double HitTestThreshold = 8.0; // pixels for section boundary hit-testing
    private bool _scrollbarDragging;
    private double _scrollbarDragStartX;
    private double _scrollbarDragStartViewStart;
    private bool _isDraggingSection;
    private SectionEntry? _dragSection;
    // --- Styled Properties ---

    public static readonly StyledProperty<float[]> SamplesProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, float[]>(nameof(Samples), []);

    public float[] Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public static readonly StyledProperty<double> AudioDurationProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, double>(nameof(AudioDuration), 1.0);

    public double AudioDuration
    {
        get => GetValue(AudioDurationProperty);
        set => SetValue(AudioDurationProperty, value);
    }

    public static readonly StyledProperty<double> ZeroBeatTimeProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, double>(nameof(ZeroBeatTime), 0.0);

    public double ZeroBeatTime
    {
        get => GetValue(ZeroBeatTimeProperty);
        set => SetValue(ZeroBeatTimeProperty, value);
    }

    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, double>(nameof(Bpm), 120.0);

    public double Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public static readonly StyledProperty<int> BeatsPerMeasureProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, int>(nameof(BeatsPerMeasure), 4);

    public int BeatsPerMeasure
    {
        get => GetValue(BeatsPerMeasureProperty);
        set => SetValue(BeatsPerMeasureProperty, value);
    }

    public static readonly StyledProperty<double> ViewStartProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, double>(nameof(ViewStart), 0.0);

    public double ViewStart
    {
        get => GetValue(ViewStartProperty);
        set => SetValue(ViewStartProperty, value);
    }

    public static readonly StyledProperty<double> ViewEndProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, double>(nameof(ViewEnd), 0.0);

    public double ViewEnd
    {
        get => GetValue(ViewEndProperty);
        set => SetValue(ViewEndProperty, value);
    }

    public static readonly StyledProperty<double> PlayheadTimeProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, double>(nameof(PlayheadTime), -1.0);

    public double PlayheadTime
    {
        get => GetValue(PlayheadTimeProperty);
        set => SetValue(PlayheadTimeProperty, value);
    }

    public static readonly StyledProperty<SongSectionType> PaintTypeProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, SongSectionType>(nameof(PaintType), SongSectionType.Verse);

    /// <summary>The section type that will be painted on left-click.</summary>
    public SongSectionType PaintType
    {
        get => GetValue(PaintTypeProperty);
        set => SetValue(PaintTypeProperty, value);
    }

    public static readonly StyledProperty<ObservableCollection<SectionEntry>?> SectionsProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, ObservableCollection<SectionEntry>?>(nameof(Sections));

    /// <summary>The sections collection to paint on. Bound to the ViewModel's Sections.</summary>
    public ObservableCollection<SectionEntry>? Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public static readonly StyledProperty<SectionEntry?> SelectedSectionProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, SectionEntry?>(nameof(SelectedSection));

    /// <summary>The currently selected section. Bound TwoWay to the ViewModel's SelectedSection.</summary>
    public SectionEntry? SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    public static readonly StyledProperty<int> StartBeatProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, int>(nameof(StartBeat), 0);

    /// <summary>First beat of the padded song range (typically negative).</summary>
    public int StartBeat
    {
        get => GetValue(StartBeatProperty);
        set => SetValue(StartBeatProperty, value);
    }

    public static readonly StyledProperty<int> EndBeatProperty =
        AvaloniaProperty.Register<SectionWaveformEditor, int>(nameof(EndBeat), 0);

    /// <summary>Last beat of the padded song range (positive).</summary>
    public int EndBeat
    {
        get => GetValue(EndBeatProperty);
        set => SetValue(EndBeatProperty, value);
    }

    // --- Section color cache ---
    private static readonly Dictionary<SongSectionType, Color> _sectionColors = [];

    // --- Drawing resources ---
    private static readonly Pen _waveformPen = new(new SolidColorBrush(Colors.DarkGray), 1);
    private static readonly SolidColorBrush _waveformFill = new(Colors.LightGray, 0.5);
    private static readonly Pen _beatPen = new(new SolidColorBrush(Colors.White, 0.25), 1);
    private static readonly Pen _measurePen = new(new SolidColorBrush(Colors.White, 0.45), 1);
    private static readonly Pen _playheadPen = new(new SolidColorBrush(Colors.Lime), 2);
    private static readonly Pen _startBeatPen = new(new SolidColorBrush(Colors.Cyan), 2) { DashStyle = DashStyle.Dash };
    private static readonly Pen _endBeatPen = new(new SolidColorBrush(Colors.Orange), 2) { DashStyle = DashStyle.Dash };
    private static readonly SolidColorBrush _measureEvenBrush = new(Color.FromArgb(18, 255, 255, 255));
    private static readonly SolidColorBrush _measureOddBrush = new(Color.FromArgb(8, 255, 255, 255));
    private static readonly SolidColorBrush _measureErrorBrush = new(Color.FromArgb(25, 255, 60, 60));
    private static readonly SolidColorBrush _scrollTrackBrush = new(Color.FromRgb(20, 20, 20));
    private static readonly SolidColorBrush _scrollThumbBrush = new(Color.FromRgb(80, 80, 80));
    private static readonly SolidColorBrush _scrollThumbHoverBrush = new(Color.FromRgb(110, 110, 110));

    static SectionWaveformEditor()
    {
        AffectsRender<SectionWaveformEditor>(
            SamplesProperty, AudioDurationProperty, ZeroBeatTimeProperty,
            BpmProperty, BeatsPerMeasureProperty, ViewStartProperty, ViewEndProperty,
            PlayheadTimeProperty, SectionsProperty, SelectedSectionProperty,
            StartBeatProperty, EndBeatProperty);

        // Pre-cache section colors from the ColorAttribute on the enum
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
            ColorAttribute? attr = field?.GetCustomAttribute<ColorAttribute>();
            _sectionColors[type] = attr != null
                ? Color.FromRgb(attr.R, attr.G, attr.B)
                : Colors.Gray;
        }
    }

    // Re-render when the collection changes; auto-set view to padded range
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SectionsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnSectionsChanged;

            if (change.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnSectionsChanged;

            InvalidateVisual();
        }

        // When StartBeat or EndBeat change, auto-set the visible range to the padded window
        if ((change.Property == StartBeatProperty || change.Property == EndBeatProperty) && Bpm > 0)
        {
            double beatDuration = 60.0 / Bpm;
            double startTime = ZeroBeatTime + (StartBeat * beatDuration);
            double endTime = ZeroBeatTime + (EndBeat * beatDuration);
            if (endTime > startTime)
            {
                ViewStart = Math.Max(0, startTime);
                ViewEnd = Math.Min(AudioDuration, endTime);
            }
        }
    }

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    // --- Input handling ---

    /// <summary>Height available for the waveform area (excludes scrollbar).</summary>
    private double WaveformHeight => Math.Max(0, Bounds.Height - ScrollBarHeight);

    private bool IsInScrollbar(double y) => y >= WaveformHeight;

    private double GetVisibleEnd() => ViewEnd > 0 ? ViewEnd : AudioDuration;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint pt = e.GetCurrentPoint(this);

        if (pt.Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);

            if (IsInScrollbar(pt.Position.Y))
            {
                _scrollbarDragging = true;
                _scrollbarDragStartX = pt.Position.X;
                _scrollbarDragStartViewStart = ViewStart;
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+click: set playhead position
                SetPlayheadFromPixel(pt.Position.X);
            }
            else
            {
                // Check if click is near an existing section boundary
                SectionEntry? hitSection = HitTestSectionBoundary(pt.Position.X);
                if (hitSection != null)
                {
                    // Select the section and start dragging it
                    SelectedSection = hitSection;
                    _isDraggingSection = true;
                    _dragSection = hitSection;
                }
                else
                {
                    // Deselect and place a new section at the nearest beat
                    SelectedSection = null;
                    _isDraggingSection = false;
                    _dragSection = null;

                    double beat = PixelToBeat(pt.Position.X);
                    if (!double.IsNaN(beat))
                    {
                        beat = Math.Round(beat);
                        AddOrUpdateSection(beat, PaintType);
                    }
                }
            }

            e.Handled = true;
        }
        else if (pt.Properties.IsRightButtonPressed)
        {
            double beat = PixelToBeat(pt.Position.X);
            if (!double.IsNaN(beat))
            {
                beat = Math.Round(beat);
                RemoveSection(beat);
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this)
        {
            // Update cursor based on hover over section boundaries
            UpdateCursor(e.GetPosition(this).X);
            return;
        }

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
            e.Handled = true;
        }
        else if (_isDraggingSection && _dragSection != null)
        {
            // Move the dragged section to the nearest beat under the cursor
            double beat = PixelToBeat(pt.Position.X);
            if (!double.IsNaN(beat))
            {
                beat = Math.Round(beat);
                if (DataContext is ISongEditorViewModel)
                {
                    // Section editing moved to timeline; this control is no longer used.
                    InvalidateVisual();
                }
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
        {
            _scrollbarDragging = false;
            _isDraggingSection = false;
            _dragSection = null;
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

        double mouseX = e.GetPosition(this).X;
        double fraction = mouseX / Bounds.Width;
        double mouseTime = visibleStart + (fraction * visibleDuration);

        double zoomFactor = e.Delta.Y > 0 ? 0.8 : 1.25;
        double newDuration = Math.Clamp(visibleDuration * zoomFactor, 0.1, AudioDuration);

        double newStart = mouseTime - (fraction * newDuration);
        double newEnd = newStart + newDuration;

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

    // --- Hit testing & helpers ---

    /// <summary>
    /// Returns the section whose boundary line is within <see cref="HitTestThreshold"/> pixels
    /// of the given x position, or null if none match.
    /// </summary>
    private SectionEntry? HitTestSectionBoundary(double x)
    {
        ObservableCollection<SectionEntry>? sections = Sections;
        if (sections == null || sections.Count == 0 || Bpm <= 0)
            return null;

        SectionEntry? closest = null;
        double closestDist = double.MaxValue;

        foreach (SectionEntry section in sections)
        {
            double px = BeatToPixel(section.StartBeat);
            if (px < 0)
                continue;

            double dist = Math.Abs(px - x);
            if (dist < HitTestThreshold && dist < closestDist)
            {
                closest = section;
                closestDist = dist;
            }
        }

        return closest;
    }

    private void UpdateCursor(double x)
    {
        SectionEntry? hit = HitTestSectionBoundary(x);
        Cursor = hit != null ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
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

    // --- Section manipulation ---

    private void AddOrUpdateSection(double beat, SongSectionType type)
    {
        // Section editing moved to timeline; this control is no longer used.
    }

    private void RemoveSection(double beat)
    {
        // Section editing moved to timeline; this control is no longer used.
    }

    // --- Coordinate helpers ---

    private double PixelToBeat(double x)
    {
        double visibleStart = ViewStart;
        double visibleEnd = GetVisibleEnd();
        double visibleDuration = visibleEnd - visibleStart;
        if (visibleDuration <= 0 || Bounds.Width <= 0 || Bpm <= 0)
            return double.NaN;

        double fraction = x / Bounds.Width;
        double time = visibleStart + (fraction * visibleDuration);
        double beatDuration = 60.0 / Bpm;
        return (time - ZeroBeatTime) / beatDuration;
    }

    private double BeatToPixel(double beat)
    {
        double visibleStart = ViewStart;
        double visibleEnd = GetVisibleEnd();
        double visibleDuration = visibleEnd - visibleStart;
        if (visibleDuration <= 0 || Bounds.Width <= 0 || Bpm <= 0)
            return -1;

        double beatDuration = 60.0 / Bpm;
        double time = ZeroBeatTime + (beat * beatDuration);
        return (time - visibleStart) / visibleDuration * Bounds.Width;
    }

    // --- Rendering ---

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

        // Draw section colored regions (behind waveform)
        SectionWaveformSectionRenderer.DrawRegions(
            context, Sections, _sectionColors, Bpm, ZeroBeatTime, EndBeat, AudioDuration,
            width, height, visibleStart, visibleEnd, visibleDuration);

        // Draw waveform envelope
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

        // Draw beat grid
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

        // Draw section boundary markers
        SectionWaveformSectionRenderer.DrawBoundaries(
            context, Sections, SelectedSection, _sectionColors, Bpm, ZeroBeatTime,
            width, height, visibleStart, visibleDuration);

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

        // Draw playhead
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