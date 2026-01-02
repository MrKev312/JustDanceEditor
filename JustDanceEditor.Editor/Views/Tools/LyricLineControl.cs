using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

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
    private List<(ClipViewModel Clip, string Text, double BaseWidth)> _measuredSyllables = [];
    private double _measuredTotalWidth = 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LineProperty)
        {
            if (_lastLine != null)
            {
                foreach (ClipViewModel c in _lastLine.Clips)
                    c.PropertyChanged -= Clip_PropertyChanged;
            }

            _lastLine = Line;

            if (_lastLine != null)
            {
                foreach (ClipViewModel c in _lastLine.Clips)
                    c.PropertyChanged += Clip_PropertyChanged;
            }

            ClearMeasurementCache();
            InvalidateVisual();
        }
    }

    private void Clip_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)) or (nameof(KaraokeClipViewModel.Lyrics)))
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

        Typeface typeface = new("Arial", FontStyle.Normal, FontWeight.Bold);

        foreach (ClipViewModel c in Line.Clips)
        {
            string text = (c as KaraokeClipViewModel)?.Lyrics ?? "";
            FormattedText ft = new(
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

        Typeface typeface = new("Arial", FontStyle.Normal, FontWeight.Bold);
        double baseFontSize = Bounds.Height * 0.8;

        double totalWidth = _measuredTotalWidth;

        // 2. Calculate scaling to fit within bounds (with 20px margin on each side)
        double scale = Math.Min(1.0, (Bounds.Width - 40) / Math.Max(1, totalWidth));
        double fontSize = baseFontSize * scale;

        // 3. Build final formatted texts using cached text strings
        double scaledWidth = 0;
        List<(ClipViewModel Clip, string Text, FormattedText FT)> finalSyllables = new();
        foreach ((ClipViewModel Clip, string Text, double BaseWidth) s in _measuredSyllables)
        {
            FormattedText ft = new(
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

        foreach ((ClipViewModel Clip, string Text, FormattedText FT) s in finalSyllables)
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
            FormattedText finalFt = new(
                s.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fillBrush
            );

            Geometry? textGeometry = finalFt.BuildGeometry(new Point(x, y));
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

        Point pt = e.GetCurrentPoint(this).Position;

        // Determine layout same as in Render to find which syllable was clicked
        EnsureMeasurements();

        Typeface typeface = new("Arial", FontStyle.Normal, FontWeight.Bold);
        double baseFontSize = Bounds.Height * 0.8;
        double scale = Math.Min(1.0, (Bounds.Width - 40) / Math.Max(1, _measuredTotalWidth));
        double fontSize = baseFontSize * scale;

        var syllables = _measuredSyllables.Select(s =>
        {
            FormattedText ft = new(s.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);
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
            try
            {
                e.Pointer.Capture(this);
            }
            catch { }

            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging || _draggingClip == null)
            return;

        Point pt = e.GetCurrentPoint(this).Position;
        double deltaX = pt.X - _dragStartPointerX;

        // Find timeline view model to get pixels-per-beat mapping
        Visual? visualParent = this.GetVisualParent();
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

        double unconstrained = _dragOriginalStartBeat + deltaBeats;
        if (unconstrained < 0)
            unconstrained = 0;

        // Use SnappingService to compute best start
        double newStart;
        if (vm != null)
        {
            // Exclude the dragging syllable/clip so it doesn't snap to itself
            newStart = SnappingService.FindSnapBeat(unconstrained, vm, new[] { _draggingClip });
        }
        else
        {
            newStart = SnappingService.FindSnapBeat(unconstrained, TimelineEditorViewModelPlaceholder.Instance);
        }

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
            try
            {
                e.Pointer.Capture(null);
            }
            catch { }

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