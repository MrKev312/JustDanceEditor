using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;

using SkiaSharp;

using System;
using System.ComponentModel;

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

    public static readonly StyledProperty<bool> IsTextLeftAlignedProperty =
        AvaloniaProperty.Register<LyricLineControl, bool>(nameof(IsTextLeftAligned));

    public bool IsTextLeftAligned
    {
        get => GetValue(IsTextLeftAlignedProperty);
        set => SetValue(IsTextLeftAlignedProperty, value);
    }

    static LyricLineControl()
    {
        AffectsRender<LyricLineControl>(LineProperty, CurrentBeatProperty, TargetColorProperty, IsTextLeftAlignedProperty);
    }

    private LyricLineViewModel? _lastLine;

    // Drag state for lyric syllables
    private ClipViewModel? _draggingClip;
    private double _dragStartPointerX;
    private double _dragOriginalStartBeat;
    private bool _isDragging;

    private SkiaLyricLineLayout? _renderLayout;

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

            ClearRenderLayout();
            InvalidateVisual();
        }
    }

    private void Clip_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)) or (nameof(KaraokeClipViewModel.Lyrics)))
        {
            ClearRenderLayout();
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    private void ClearRenderLayout()
    {
        _renderLayout?.Dispose();
        _renderLayout = null;
    }

    private SkiaLyricLineLayout? EnsureRenderLayout()
    {
        LyricLineViewModel? line = Line;
        if (line == null || line.Clips.Count == 0)
            return null;

        double boundsWidth = Bounds.Width;
        double boundsHeight = Bounds.Height;
        if (!HasRenderableBounds(boundsWidth, boundsHeight))
            return null;

        Rect layoutBounds = new(0, 0, boundsWidth, boundsHeight);
        if (_renderLayout?.Matches(line, layoutBounds, IsTextLeftAligned) == true)
            return _renderLayout;

        ClearRenderLayout();
        _renderLayout = SkiaLyricLineLayout.Create(line, layoutBounds, IsTextLeftAligned);
        return _renderLayout;
    }

    public override void Render(DrawingContext context)
    {
        if (Line == null || Line.Clips.Count == 0)
            return;

        if (!HasRenderableBounds(Bounds.Width, Bounds.Height))
            return;

        SkiaLyricLineLayout? layout = EnsureRenderLayout();
        if (layout == null)
            return;

        context.Custom(new SkiaLyricDrawOperation(new Rect(Bounds.Size), layout, CurrentBeat, TargetColor));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Line == null || Line.Clips.Count == 0)
            return;

        if (!HasRenderableBounds(Bounds.Width, Bounds.Height))
            return;

        Point pt = e.GetCurrentPoint(this).Position;

        SkiaLyricLineLayout? layout = EnsureRenderLayout();
        if (layout == null)
            return;

        ClipViewModel? found = null;
        foreach (SkiaLyricSyllable syllable in layout.Syllables)
        {
            if (pt.X >= syllable.HitBounds.Left && pt.X <= syllable.HitBounds.Right)
            {
                found = syllable.Clip;
                break;
            }
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

        ClearRenderLayout();
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearRenderLayout();
    }

    private sealed class SkiaLyricDrawOperation(Rect bounds, SkiaLyricLineLayout layout, double currentBeat, Color targetColor) : ICustomDrawOperation
    {
        private readonly SkiaLyricLineLayout _layout = layout.AddReference();
        private readonly double _currentBeat = currentBeat;
        private readonly Color _targetColor = targetColor;
        private bool _disposed;

        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            canvas.Save();
            try
            {
                canvas.ClipRect(Bounds.ToSKRect(), SKClipOperation.Intersect, antialias: false);
                SkiaLyricRenderer.Render(canvas, _layout, _currentBeat, _targetColor, opacity: 1.0);
            }
            finally
            {
                canvas.Restore();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _layout.Dispose();
            _disposed = true;
        }
    }

    private static bool HasRenderableBounds(double width, double height)
        => width > 0
            && height > 0
            && !double.IsNaN(width)
            && !double.IsNaN(height)
            && !double.IsInfinity(width)
            && !double.IsInfinity(height);
}