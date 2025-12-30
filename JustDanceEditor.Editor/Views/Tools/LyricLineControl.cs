using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI.Timelines;
using System;
using System.Linq;
using System.ComponentModel;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Tools;

public class LyricLineControl : Control
{
    public static readonly StyledProperty<LyricLineViewModel?> LineProperty =
        AvaloniaProperty.Register<LyricLineControl, LyricLineViewModel?>(nameof(Line));

    public LyricLineViewModel? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public static readonly StyledProperty<double> CurrentBeatProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(CurrentBeat));

    public double CurrentBeat
    {
        get => GetValue(CurrentBeatProperty);
        set => SetValue(CurrentBeatProperty, value);
    }

    public static readonly StyledProperty<Color> TargetColorProperty =
        AvaloniaProperty.Register<LyricLineControl, Color>(nameof(TargetColor), Colors.SkyBlue);

    public Color TargetColor
    {
        get => GetValue(TargetColorProperty);
        set => SetValue(TargetColorProperty, value);
    }

    static LyricLineControl()
    {
        AffectsRender<LyricLineControl>(LineProperty, CurrentBeatProperty, TargetColorProperty);
    }

    private LyricLineViewModel? _lastLine;

    // Drag state for lyric syllables
    private ClipViewModel? _draggingClip;
    private double _dragStartPointerX;
    private double _dragOriginalStartBeat;
    private bool _isDragging;

    // Caching measurement results to speed Render
    private LyricLineViewModel? _measuredLine;
    private double _measuredBoundsWidth = -1;
    private double _measuredBaseFontSize = -1;
    private List<(ClipViewModel Clip, string Text, double BaseWidth)> _measuredSyllables = new();
    private double _measuredTotalWidth = 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LineProperty)
        {
            if (_lastLine != null)
            {
                foreach (var c in _lastLine.Clips)
                    c.PropertyChanged -= Clip_PropertyChanged;
            }

            _lastLine = Line;

            if (_lastLine != null)
            {
                foreach (var c in _lastLine.Clips)
                    c.PropertyChanged += Clip_PropertyChanged;
            }

            ClearMeasurementCache();
            InvalidateVisual();
        }
    }

    private void Clip_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipViewModel.StartBeat) || e.PropertyName == nameof(ClipViewModel.DurationBeats))
        {
            ClearMeasurementCache();
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    private void ClearMeasurementCache()
    {
        _measuredLine = null;
        _measuredBoundsWidth = -1;
        _measuredBaseFontSize = -1;
        _measuredSyllables.Clear();
        _measuredTotalWidth = 0;
    }

    private void EnsureMeasurements()
    {
        if (Line == null)
            return;

        double boundsWidth = Bounds.Width;
        double baseFontSize = Bounds.Height * 0.8;

        if (_measuredLine == Line && Math.Abs(_measuredBoundsWidth - boundsWidth) < 0.1 && Math.Abs(_measuredBaseFontSize - baseFontSize) < 0.1)
            return; // cache valid

        _measuredLine = Line;
        _measuredBoundsWidth = boundsWidth;
        _measuredBaseFontSize = baseFontSize;

        _measuredSyllables.Clear();
        _measuredTotalWidth = 0;

        var typeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);

        foreach (var c in Line.Clips)
        {
            string text = (c.RawClip as KaraokeClip)?.Lyrics ?? "";
            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                baseFontSize,
                Brushes.White
            );
            double w = ft.WidthIncludingTrailingWhitespace;
            _measuredSyllables.Add((c, text, w));
            _measuredTotalWidth += w;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Line == null || Line.Clips.Count == 0)
            return;

        EnsureMeasurements();

        var typeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
        double baseFontSize = Bounds.Height * 0.8;

        double totalWidth = _measuredTotalWidth;

        // 2. Calculate scaling to fit within bounds (with 20px margin on each side)
        double scale = Math.Min(1.0, (Bounds.Width - 40) / Math.Max(1, totalWidth));
        double fontSize = baseFontSize * scale;

        // 3. Build final formatted texts using cached text strings
        double scaledWidth = 0;
        var finalSyllables = new List<(ClipViewModel Clip, string Text, FormattedText FT)>();
        foreach (var s in _measuredSyllables)
        {
            var ft = new FormattedText(
                s.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White
            );
            finalSyllables.Add((s.Clip, s.Text, ft));
            scaledWidth += ft.WidthIncludingTrailingWhitespace;
        }

        double x = (Bounds.Width - scaledWidth) / 2.0;
        double y = (Bounds.Height - fontSize) / 2.0;

        double beat = CurrentBeat;

        foreach (var s in finalSyllables)
        {
            double start = s.Clip.StartBeat;
            double end = start + s.Clip.DurationBeats;

            IBrush fillBrush;

            if (beat >= end)
            {
                fillBrush = new SolidColorBrush(TargetColor);
            }
            else if (beat <= start)
            {
                fillBrush = Brushes.White;
            }
            else
            {
                double progress = (beat - start) / (end - start);
                fillBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(TargetColor, progress),
                        new GradientStop(Colors.White, progress)
                    }
                };
            }

            // Create final FormattedText and generate geometry for outline rendering
            var finalFt = new FormattedText(
                s.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fillBrush
            );

            var textGeometry = finalFt.BuildGeometry(new Point(x, y));
            if (textGeometry != null)
            {
                // Draw outline first
                context.DrawGeometry(null, new Pen(Brushes.Black, Math.Max(1.0, fontSize * 0.05), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), textGeometry);
                // Then draw the fill
                context.DrawGeometry(fillBrush, null, textGeometry);
            }

            x += finalFt.WidthIncludingTrailingWhitespace;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Line == null || Line.Clips.Count == 0)
            return;

        var pt = e.GetCurrentPoint(this).Position;

        // Determine layout same as in Render to find which syllable was clicked
        EnsureMeasurements();

        var typeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
        double baseFontSize = Bounds.Height * 0.8;
        double scale = Math.Min(1.0, (Bounds.Width - 40) / Math.Max(1, _measuredTotalWidth));
        double fontSize = baseFontSize * scale;

        var syllables = _measuredSyllables.Select(s => {
            var ft = new FormattedText(s.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);
            return new { s.Clip, s.Text, FT = ft };
        }).ToList();

        double scaledWidth = syllables.Sum(s => s.FT.WidthIncludingTrailingWhitespace);
        double x = (Bounds.Width - scaledWidth) / 2.0;

        ClipViewModel? found = null;
        foreach (var s in syllables)
        {
            double w = s.FT.WidthIncludingTrailingWhitespace;
            if (pt.X >= x && pt.X <= x + w)
            {
                found = s.Clip;
                break;
            }
            x += w;
        }

        if (found != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggingClip = found;
            _dragStartPointerX = pt.X;
            _dragOriginalStartBeat = found.StartBeat;
            _isDragging = true;
            try { e.Pointer.Capture(this); } catch { }
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging || _draggingClip == null)
            return;

        var pt = e.GetCurrentPoint(this).Position;
        double deltaX = pt.X - _dragStartPointerX;

        // Find timeline view model to get pixels-per-beat mapping
        var visualParent = this.GetVisualParent();
        TimelineEditorViewModel? vm = null;
        while (visualParent != null)
        {
            if (visualParent is Control c && c.DataContext is TimelineEditorViewModel t)
            {
                vm = t;
                break;
            }
            visualParent = visualParent.GetVisualParent();
        }

        double pixelsPerBeat = vm?.PixelsPerBeat ?? 50.0; // fallback

        double deltaBeats = deltaX / pixelsPerBeat;

        double newStart = _dragOriginalStartBeat + deltaBeats;
        if (newStart < 0) newStart = 0;

        _draggingClip.StartBeat = newStart;

        ClearMeasurementCache();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            _draggingClip = null;
            try { e.Pointer.Capture(null); } catch { }
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _isDragging = false;
        _draggingClip = null;
    }
}
