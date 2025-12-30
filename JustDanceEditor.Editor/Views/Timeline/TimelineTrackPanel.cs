using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimelineTrackPanel : Control
{
    // --- Dependency Properties ---

    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, double>(nameof(PixelsPerBeat), 50.0);

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<ClipViewModel>> ClipsProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IEnumerable<ClipViewModel>>(nameof(Clips));

    public IEnumerable<ClipViewModel> Clips
    {
        get => GetValue(ClipsProperty);
        set => SetValue(ClipsProperty, value);
    }

    public static readonly StyledProperty<int> BeatOffsetProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, int>(nameof(BeatOffset), 0);

    public int BeatOffset
    {
        get => GetValue(BeatOffsetProperty);
        set => SetValue(BeatOffsetProperty, value);
    }

    public static readonly StyledProperty<double> MaxBeatProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, double>(nameof(MaxBeat), 0.0);

    public double MaxBeat
    {
        get => GetValue(MaxBeatProperty);
        set => SetValue(MaxBeatProperty, value);
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IBrush?>(nameof(Background));

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    // --- Caches & Resources ---

    private static readonly Pen _linePen = new(Brushes.White, 1);
    private static readonly Typeface _textTypeface = new("Arial");
    private static readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    // Bitmap Cache: Path -> Bitmap
    private static readonly ConcurrentDictionary<string, Bitmap> _bitmapCache = new();
    private static readonly ConcurrentDictionary<string, bool> _pendingLoads = new();

    // Simple FormattedText cache to avoid recreating layouts repeatedly when rendering many clips
    private readonly Dictionary<(ClipViewModel clip, double fontSize), FormattedText> _textCache = new();

    // Track per-clip handlers so external updates invalidate visuals
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = new();

    // Dragging state
    private ClipViewModel? _draggingClip;
    private double _dragStartPointerX;
    private double _dragOriginalStartBeat;
    private bool _isDragging;

    static TimelineTrackPanel()
    {
        AffectsRender<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty,
            BackgroundProperty);

        AffectsMeasure<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty);

        ClipsProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.OnClipsChanged(e));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PixelsPerBeatProperty)
        {
            // zoom changed -> cached text sizes invalid
            _textCache.Clear();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void OnClipsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged oldObs)
        {
            oldObs.CollectionChanged -= OnCollectionChanged;

            // remove handlers for previous collection
            if (e.OldValue is IEnumerable<ClipViewModel> oldClips)
            {
                foreach (var c in oldClips)
                    RemoveClipHandler(c);
            }
        }

        if (e.NewValue is INotifyCollectionChanged newObs)
        {
            newObs.CollectionChanged += OnCollectionChanged;

            // subscribe handlers for new collection
            if (e.NewValue is IEnumerable<ClipViewModel> newClips)
            {
                foreach (var c in newClips)
                    AddClipHandler(c);
            }
        }

        // Clips changed -> clear cache
        _textCache.Clear();

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe new clips
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ClipViewModel clip)
                    AddClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ClipViewModel clip)
                    RemoveClipHandler(clip);
            }
        }

        // Items changed -> clear cache
        _textCache.Clear();

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void AddClipHandler(ClipViewModel clip)
    {
        if (clip == null) return;
        if (_clipHandlers.ContainsKey(clip)) return;

        PropertyChangedEventHandler handler = (s, e) =>
        {
            if (e.PropertyName == nameof(ClipViewModel.StartBeat) || e.PropertyName == nameof(ClipViewModel.DurationBeats) || e.PropertyName == nameof(ClipViewModel.ImagePath) || e.PropertyName == nameof(ClipViewModel.Name))
            {
                // clear text cache for this clip
                var keys = _textCache.Keys.Where(k => k.clip == clip).ToList();
                foreach (var k in keys) _textCache.Remove(k);

                // Ensure arrange/render happens on UI thread
                Dispatcher.UIThread.Post(() => { InvalidateMeasure(); InvalidateVisual(); });
            }
        };

        clip.PropertyChanged += handler;
        _clipHandlers[clip] = handler;
    }

    private void RemoveClipHandler(ClipViewModel clip)
    {
        if (clip == null) return;
        if (_clipHandlers.TryGetValue(clip, out var handler))
        {
            clip.PropertyChanged -= handler;
            _clipHandlers.Remove(clip);
        }
    }

    // Helper to get or create cached FormattedText
    private FormattedText GetFormattedText(ClipViewModel clip, string text, double fontSize, double maxWidth, double maxHeight)
    {
        var key = (clip, fontSize);
        if (_textCache.TryGetValue(key, out var ft))
            return ft;

        ft = new FormattedText(
            text,
            _culture,
            FlowDirection.LeftToRight,
            _textTypeface,
            fontSize,
            Brushes.Black)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = maxHeight,
            Trimming = TextTrimming.CharacterEllipsis
        };

        // store in cache
        _textCache[key] = ft;
        return ft;
    }

    // --- Layout ---

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = MaxBeat * PixelsPerBeat;

        if (Clips != null)
        {
            foreach (var clip in Clips)
            {
                double endX = (clip.StartBeat - BeatOffset + clip.DurationBeats) * PixelsPerBeat;
                if (endX > width) width = endX;
            }
        }

        return new Size(Math.Max(0, width), availableSize.Height);
    }

    // --- Rendering ---

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;

        // Compute visible beat range once to avoid repeated work
        double visibleStartBeat = offset - 1; // small left margin
        double visibleEndBeat = offset + (bounds.Width / Math.Max(1.0, ppb)) + 1; // small right margin

        // 1. Draw Background
        if (Background != null)
        {
            context.FillRectangle(Background, new Rect(bounds.Size));
        }

        // 2. Draw Marker Lines
        if (BeatOffset != 0)
        {
            double x0 = -offset * ppb;
            if (x0 > -1 && x0 < bounds.Width + 1)
                context.DrawLine(_linePen, new Point(x0, 0), new Point(x0, bounds.Height));
        }
        else
        {
            context.DrawLine(_linePen, new Point(0, 0), new Point(0, bounds.Height));
        }

        if (MaxBeat > 0)
        {
            double xEnd = MaxBeat * ppb;
            if (xEnd > -1 && xEnd < bounds.Width + 1)
                context.DrawLine(_linePen, new Point(xEnd, 0), new Point(xEnd, bounds.Height));
        }

        if (Clips == null) return;

        // 3. Draw Clips
        bool drawText = ppb > 10;

        // Iterate clips but skip those entirely outside visible beat range (faster when zoomed in)
        foreach (var clip in Clips)
        {
            double clipStart = clip.StartBeat;
            double clipEnd = clip.StartBeat + clip.DurationBeats;

            if (clipEnd < visibleStartBeat || clipStart > visibleEndBeat)
                continue;

            double startX = (clipStart - offset) * ppb;
            double width = clip.DurationBeats * ppb;
            double endX = startX + width;

            // Culling based on pixels too (additional guard)
            if (endX < 0 || startX > bounds.Width)
                continue;

            // Draw Clip Background
            var rect = new Rect(startX, 2, Math.Max(0, width), Math.Max(1, bounds.Height - 4));
            context.FillRectangle(new SolidColorBrush(clip.BackgroundColor), rect);

            // Draw outline around clip
            var outlinePen = new Pen(Brushes.Black, Math.Max(1.0, rect.Height * 0.05), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawRectangle(null, outlinePen, rect);

            // Draw Image (Pictogram)
            if (clip.ImagePath != null)
            {
                if (_bitmapCache.TryGetValue(clip.ImagePath, out Bitmap? bmp))
                {
                    // Draw bitmap to fit height, centered horizontally
                    double aspect = bmp.Size.Width / bmp.Size.Height;
                    double drawHeight = rect.Height;
                    double drawWidth = drawHeight * aspect;

                    // Skip drawing tiny images when highly zoomed out
                    if (drawWidth >= 2 && drawHeight >= 2)
                    {
                        // Center image in the clip rect
                        double imgX = startX + (width - drawWidth) / 2;
                        double imgY = rect.Y;

                        var destRect = new Rect(imgX, imgY, drawWidth, drawHeight);

                        using (context.PushClip(rect))
                        {
                            context.DrawImage(bmp, new Rect(bmp.Size), destRect);
                        }
                    }
                }
                else
                {
                    ScheduleBitmapLoad(clip.ImagePath);
                }
            }
            // Draw Text (Only if NOT a pictogram/image)
            else if (drawText && width > 30 && !string.IsNullOrEmpty(clip.Name))
            {
                // Use cached FormattedText to avoid allocations
                var ft = GetFormattedText(clip, clip.Name, 12, width, rect.Height);

                // Center horizontally: startX + (clipWidth - textWidth) / 2
                // Center vertically:   rect.Y + (clipHeight - textHeight) / 2
                double textX = startX + (width - ft.Width) / 2;
                double textY = rect.Y + (rect.Height - ft.Height) / 2;

                // Ensure we don't draw outside the clip if centered text pushes out
                if (textX < startX) textX = startX;

                context.DrawText(ft, new Point(textX, textY));
            }
        }
    }

    private void ScheduleBitmapLoad(string path)
    {
        if (_pendingLoads.ContainsKey(path)) return;
        _pendingLoads.TryAdd(path, true);

        Task.Run(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    using var fs = File.OpenRead(path);
                    var bmp = Bitmap.DecodeToWidth(fs, 200);

                    _bitmapCache.TryAdd(path, bmp);

                    Dispatcher.UIThread.Post(() => {
                        _pendingLoads.TryRemove(path, out _);
                        InvalidateVisual();
                    });
                }
            }
            catch
            {
                _pendingLoads.TryRemove(path, out _);
            }
        });
    }

    // --- Interaction ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this).Position;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // Double-click to seek (existing behavior)
        if (e.ClickCount == 2 && Clips != null)
        {
            ClipViewModel? targetClip = null;

            foreach (var c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                if (point.X >= x && point.X <= x + w)
                {
                    targetClip = c;
                }
            }

            if (targetClip != null)
            {
                var visualParent = this.GetVisualParent();
                while (visualParent != null)
                {
                    if (visualParent is Control c && c.DataContext is TimelineEditorViewModel vm)
                    {
                        vm.Playback.SeekToBeat(targetClip.StartBeat);
                        e.Handled = true;
                        return;
                    }
                    visualParent = visualParent.GetVisualParent();
                }
            }
        }

        // Start potential drag on left-button press
        if (Clips != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClipViewModel? targetClip = null;

            foreach (var c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                if (point.X >= x && point.X <= x + w)
                {
                    targetClip = c;
                }
            }

            if (targetClip != null)
            {
                _draggingClip = targetClip;
                _dragStartPointerX = point.X;
                _dragOriginalStartBeat = targetClip.StartBeat;
                _isDragging = true;
                try { e.Pointer.Capture(this); } catch { }
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging || _draggingClip == null)
            return;

        var point = e.GetCurrentPoint(this).Position;
        double deltaX = point.X - _dragStartPointerX;
        double deltaBeats = deltaX / PixelsPerBeat;

        double newStart = _dragOriginalStartBeat + deltaBeats;
        if (newStart < 0) newStart = 0;

        // Try to apply snapping if the timeline VM exposes options
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

        if (vm != null)
        {
            // Only perform snapping if at least one snapping mode is enabled
            if (vm.SnapToGrid || vm.SnapToCurrentTimeMarker)
            {
                var candidates = new List<double>();

                // Grid snapping candidates (floor/round/ceil for start and end)
                if (vm.SnapToGrid)
                {
                    double grid = vm.SnapGridSize;
                    if (grid <= 0) grid = 1.0;

                    // start candidates
                    double startFlo = Math.Floor(newStart / grid) * grid;
                    double startRound = Math.Round(newStart / grid) * grid;
                    double startCeil = Math.Ceiling(newStart / grid) * grid;
                    candidates.Add(startFlo);
                    candidates.Add(startRound);
                    candidates.Add(startCeil);

                    // end candidates -> derive start from snapped end
                    double endBeat = newStart + _draggingClip.DurationBeats;
                    double endFlo = Math.Floor(endBeat / grid) * grid - _draggingClip.DurationBeats;
                    double endRound = Math.Round(endBeat / grid) * grid - _draggingClip.DurationBeats;
                    double endCeil = Math.Ceiling(endBeat / grid) * grid - _draggingClip.DurationBeats;
                    candidates.Add(endFlo);
                    candidates.Add(endRound);
                    candidates.Add(endCeil);
                }

                // Playhead snapping candidates
                if (vm.SnapToCurrentTimeMarker)
                {
                    double current = vm.CurrentBeat;
                    candidates.Add(current); // align start to playhead
                    candidates.Add(current - _draggingClip.DurationBeats); // align end to playhead
                }

                // If we have any candidates, pick the closest to the unconstrained newStart
                if (candidates.Count > 0)
                {
                    double best = newStart;
                    double bestDist = double.MaxValue;
                    foreach (var c in candidates)
                    {
                        double dist = Math.Abs(c - newStart);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = c;
                        }
                    }

                    newStart = best;
                }
            }
        }

        // Update model which will notify subscribers (pictogram bar, lyrics pane)
        _draggingClip.StartBeat = newStart;

        // Ensure immediate visual update
        InvalidateMeasure();
        InvalidateVisual();

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            // record undo: restore original start
            var finalClip = _draggingClip;
            var original = _dragOriginalStartBeat;
            if (finalClip != null)
            {
                var visualParent = this.GetVisualParent();
                while (visualParent != null)
                {
                    if (visualParent is Control c && c.DataContext is TimelineEditorViewModel vm)
                    {
                        double newStart = finalClip.StartBeat;
                        vm.PushUndo(
                            () => finalClip.StartBeat = original,
                            () => finalClip.StartBeat = newStart);
                        break;
                    }
                    visualParent = visualParent.GetVisualParent();
                }
            }

            _draggingClip = null;
            // Release pointer capture
            try { e.Pointer.Capture(null); } catch { }
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // Cancel any active drag state
        _isDragging = false;
        _draggingClip = null;
    }
}