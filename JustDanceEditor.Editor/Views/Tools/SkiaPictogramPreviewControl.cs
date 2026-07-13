using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using SkiaSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class SkiaPictogramPreviewControl : Control
{
    public static readonly StyledProperty<double> CurrentBeatProperty =
        AvaloniaProperty.Register<SkiaPictogramPreviewControl, double>(nameof(CurrentBeat));

    public double CurrentBeat
    {
        get => GetValue(CurrentBeatProperty);
        set => SetValue(CurrentBeatProperty, value);
    }

    public static readonly StyledProperty<TimelineEditorViewModel?> ActiveTimelineProperty =
        AvaloniaProperty.Register<SkiaPictogramPreviewControl, TimelineEditorViewModel?>(nameof(ActiveTimeline));

    public TimelineEditorViewModel? ActiveTimeline
    {
        get => GetValue(ActiveTimelineProperty);
        set => SetValue(ActiveTimelineProperty, value);
    }

    public static readonly StyledProperty<bool> AllowFadeOutOverflowProperty =
        AvaloniaProperty.Register<SkiaPictogramPreviewControl, bool>(nameof(AllowFadeOutOverflow));

    public bool AllowFadeOutOverflow
    {
        get => GetValue(AllowFadeOutOverflowProperty);
        set => SetValue(AllowFadeOutOverflowProperty, value);
    }

    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = [];
    private readonly List<PictogramClipViewModel> _sortedPictograms = [];
    private readonly List<DrawItem> _drawItems = [];
    private readonly List<HitItem> _lastHitItems = [];
    private TrackViewModel? _pictogramTrack;
    private TimelineEditorViewModel? _subscribedTimeline;
    private ClipViewModel? _draggingClip;
    private double _dragStartPointerX;
    private double _dragOriginalStartBeat;
    private double _lastScrollDuration;
    private bool _pictogramCacheDirty = true;

    static SkiaPictogramPreviewControl()
    {
        AffectsRender<SkiaPictogramPreviewControl>(CurrentBeatProperty, ActiveTimelineProperty, AllowFadeOutOverflowProperty);
        ActiveTimelineProperty.Changed.AddClassHandler<SkiaPictogramPreviewControl>((control, _) => control.OnActiveTimelineChanged());
    }

    private void OnActiveTimelineChanged()
    {
        CancelDrag();
        EnsureTimelineSubscription();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Size size = Bounds.Size;
        if (size.Width <= 1 || size.Height <= 1)
            return;

        BuildDrawItems(size, updateHitItems: IsHitTestVisible);
        Rect drawBounds = AllowFadeOutOverflow
            ? new Rect(0, -size.Height, size.Width, size.Height * 2)
            : new Rect(size);
        context.Custom(SkiaPictogramDrawOperation.Create(drawBounds, _drawItems));
    }

    private void BuildDrawItems(Size size, bool updateHitItems)
    {
        EnsureTimelineSubscription();

        _drawItems.Clear();
        if (updateHitItems)
            _lastHitItems.Clear();

        TimelineEditorViewModel? timeline = ActiveTimeline;
        TrackViewModel? track = _pictogramTrack;
        if (timeline == null || track == null)
            return;

        EnsurePictogramCache();

        double currentBeat = CurrentBeat;
        double scrollDuration = GetScrollDurationInBeats(currentBeat, timeline);
        if (scrollDuration <= 0)
            return;

        int coachCount = timeline.CoachCount;
        double defaultAspect = GetDefaultAspect(coachCount);
        double searchStartBeat = currentBeat - scrollDuration;
        double searchEndBeat = currentBeat + scrollDuration;
        int startIndex = FindFirstPictogramAtOrAfter(searchStartBeat);

        for (int i = startIndex; i < _sortedPictograms.Count; i++)
        {
            PictogramClipViewModel pictogram = _sortedPictograms[i];
            if (pictogram.StartBeat > searchEndBeat)
                break;

            if (string.IsNullOrWhiteSpace(pictogram.ImagePath))
                continue;

            double startBeat = pictogram.StartBeat;
            double beatsPerPixel = GetBeatsPerPixel(coachCount, startBeat, timeline);
            double expectedWidth = GetPictogramExpectedWidth(coachCount);
            double stopBeat = startBeat + (beatsPerPixel * expectedWidth);

            double drawX = size.Width * ((startBeat - currentBeat) / scrollDuration);
            double drawWidth = size.Width * ((stopBeat - startBeat) / scrollDuration);
            if (drawWidth <= 0 || drawX > size.Width || drawX + drawWidth < 0)
                continue;

            if (!SkiaPictogramImageCache.TryGet(pictogram.ImagePath, out SkiaPictogramImage? image))
                SkiaPictogramImageCache.ScheduleLoad(pictogram.ImagePath, InvalidateVisual);

            double aspect = image != null && image.Width > 0
                ? image.Height / (double)image.Width
                : defaultAspect;

            double drawHeight = drawWidth * aspect;
            double drawY = (size.Height - drawHeight) / 2.0;

            double offScreenLeft = drawX < 0 ? -drawX / drawWidth : 0;
            double opacity = Math.Clamp(1.0 - (1.8 * offScreenLeft), 0, 1);
            if (drawX < 0)
            {
                drawY -= 0.4 * drawHeight * offScreenLeft;
                drawX = 0;
            }

            if (opacity <= 0.01 || drawHeight <= 0)
                continue;

            Rect rect = new(drawX, drawY, drawWidth, drawHeight);
            _drawItems.Add(new DrawItem(image, rect, opacity));
            if (updateHitItems)
                _lastHitItems.Add(new HitItem(pictogram, rect));
        }

        _lastScrollDuration = scrollDuration;
    }

    private void EnsureTimelineSubscription()
    {
        if (_subscribedTimeline == ActiveTimeline)
            return;

        UnsubscribeTrack();
        _subscribedTimeline = ActiveTimeline;

        _pictogramTrack = _subscribedTimeline?.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Pictogram);
        if (_pictogramTrack == null)
            return;

        _pictogramTrack.Clips.CollectionChanged += OnPictogramClipsChanged;
        foreach (ClipViewModel clip in _pictogramTrack.Clips)
            AddClipHandler(clip);

        _pictogramCacheDirty = true;
    }

    private void OnPictogramClipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
                if (item is ClipViewModel clip)
                    RemoveClipHandler(clip);
        }

        if (e.NewItems != null)
        {
            foreach (object? item in e.NewItems)
                if (item is ClipViewModel clip)
                    AddClipHandler(clip);
        }

        _pictogramCacheDirty = true;
        InvalidateVisual();
    }

    private void AddClipHandler(ClipViewModel clip)
    {
        if (_clipHandlers.ContainsKey(clip))
            return;

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ClipViewModel.StartBeat)
                or nameof(ClipViewModel.DurationBeats)
                or nameof(ClipViewModel.ImagePath)
                or nameof(PictogramClipViewModel.PictogramId))
            {
                _pictogramCacheDirty = true;
                InvalidateVisual();
            }
        }

        clip.PropertyChanged += Handler;
        _clipHandlers[clip] = Handler;
    }

    private void RemoveClipHandler(ClipViewModel clip)
    {
        if (!_clipHandlers.Remove(clip, out PropertyChangedEventHandler? handler))
            return;

        clip.PropertyChanged -= handler;
    }

    private void UnsubscribeTrack()
    {
        if (_pictogramTrack != null)
            _pictogramTrack.Clips.CollectionChanged -= OnPictogramClipsChanged;

        foreach ((ClipViewModel clip, PropertyChangedEventHandler handler) in _clipHandlers)
            clip.PropertyChanged -= handler;

        _clipHandlers.Clear();
        _sortedPictograms.Clear();
        _pictogramCacheDirty = true;
        _pictogramTrack = null;
    }

    private void EnsurePictogramCache()
    {
        if (!_pictogramCacheDirty)
            return;

        _sortedPictograms.Clear();
        if (_pictogramTrack != null)
        {
            _sortedPictograms.AddRange(
                _pictogramTrack.Clips
                    .OfType<PictogramClipViewModel>()
                    .OrderBy(clip => clip.StartBeat));
            SkiaPictogramImageCache.Preload(_sortedPictograms.Select(clip => clip.ImagePath));
        }

        _pictogramCacheDirty = false;
    }

    private int FindFirstPictogramAtOrAfter(double beat)
    {
        int low = 0;
        int high = _sortedPictograms.Count;

        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (_sortedPictograms[mid].StartBeat < beat)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CancelDrag();
        UnsubscribeTrack();
        _subscribedTimeline = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPoint pointer = e.GetCurrentPoint(this);
        if (!pointer.Properties.IsLeftButtonPressed)
            return;

        Point point = pointer.Position;
        for (int i = _lastHitItems.Count - 1; i >= 0; i--)
        {
            HitItem item = _lastHitItems[i];
            if (!item.Bounds.Contains(point))
                continue;

            _draggingClip = item.Clip;
            _dragStartPointerX = point.X;
            _dragOriginalStartBeat = item.Clip.StartBeat;
            e.Pointer.Capture(this);
            e.Handled = true;
            break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggingClip == null || ActiveTimeline == null || Bounds.Width <= 0 || _lastScrollDuration <= 0)
            return;

        Point point = e.GetCurrentPoint(this).Position;
        double pointerDeltaX = point.X - _dragStartPointerX;
        double newStart = _dragOriginalStartBeat + (pointerDeltaX / Bounds.Width * _lastScrollDuration);

        newStart = SnappingService.FindSnapBeat(newStart, ActiveTimeline, [_draggingClip]);
        _draggingClip.StartBeat = ClampClipStartBeat(newStart, _draggingClip, ActiveTimeline);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (CompleteDrag(e.Pointer))
            e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDrag();
    }

    private bool CompleteDrag(IPointer? pointer)
    {
        ClipViewModel? clip = _draggingClip;
        TimelineEditorViewModel? timeline = ActiveTimeline;
        double originalStartBeat = _dragOriginalStartBeat;
        double finalStartBeat = clip?.StartBeat ?? originalStartBeat;

        ResetDrag();
        pointer?.Capture(null);

        if (clip == null || timeline == null)
            return false;

        if (Math.Abs(finalStartBeat - originalStartBeat) > 0.001)
        {
            timeline.PushUndo(
                undo: () => clip.StartBeat = originalStartBeat,
                redo: () => clip.StartBeat = finalStartBeat);
        }

        return true;
    }

    private void CancelDrag()
    {
        if (_draggingClip != null)
            _draggingClip.StartBeat = _dragOriginalStartBeat;

        ResetDrag();
    }

    private void ResetDrag()
    {
        _draggingClip = null;
        _dragStartPointerX = 0;
        _dragOriginalStartBeat = 0;
    }

    internal static double ClampClipStartBeat(
        double startBeat,
        ClipViewModel clip,
        TimelineEditorViewModel timeline)
    {
        double timelineStart = timeline.TimelineStructure.StartBeat;
        double latestStart = Math.Max(timelineStart, timeline.TimelineStructure.EndBeat - clip.DurationBeats);
        return Math.Clamp(startBeat, timelineStart, latestStart);
    }

    private static double GetScrollDurationInBeats(double beat, TimelineEditorViewModel timeline)
    {
        double seconds = timeline.GetPlaybackSecondsAtBeatLabel(beat);
        double futureBeat = timeline.GetBeatLabelAtPlaybackSeconds(seconds + 4.0);
        return futureBeat - beat;
    }

    private static double GetBeatsPerPixel(int coachCount, double beat, TimelineEditorViewModel timeline)
    {
        double duration = GetScrollDurationInBeats(beat, timeline);
        double scrollWidthPixels = GetScrollWidthInUaf2DCoords(coachCount) / 0.4;
        return duration / scrollWidthPixels;
    }

    private static int GetScrollWidthInUaf2DCoords(int coachCount)
        => coachCount switch
        {
            1 => 800,
            2 => 900,
            3 => 950,
            4 => 1120,
            6 => 1100,
            _ => 800
        };

    private static int GetPictogramExpectedWidth(int coachCount)
        => coachCount switch
        {
            1 => 512,
            2 or 3 or 4 => 740,
            6 => 968,
            _ => 512
        };

    private static double GetDefaultAspect(int coachCount)
        => coachCount > 1 ? 354d / 512d : 1d;

    private readonly record struct DrawItem(SkiaPictogramImage? Image, Rect Bounds, double Opacity);
    private readonly record struct HitItem(ClipViewModel Clip, Rect Bounds);

    private sealed class SkiaPictogramDrawOperation : ICustomDrawOperation
    {
        private static readonly SKSamplingOptions SamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        private readonly DrawItem[]? _rentedItems;
        private readonly int _itemCount;
        private bool _disposed;

        private SkiaPictogramDrawOperation(Rect bounds, DrawItem[]? rentedItems, int itemCount)
        {
            Bounds = bounds;
            _rentedItems = rentedItems;
            _itemCount = itemCount;
        }

        public Rect Bounds { get; }

        public static SkiaPictogramDrawOperation Create(Rect bounds, List<DrawItem> items)
        {
            int itemCount = items.Count;
            if (itemCount == 0)
                return new SkiaPictogramDrawOperation(bounds, null, 0);

            DrawItem[] rentedItems = ArrayPool<DrawItem>.Shared.Rent(itemCount);
            CollectionsMarshal.AsSpan(items).CopyTo(rentedItems);
            return new SkiaPictogramDrawOperation(bounds, rentedItems, itemCount);
        }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            if (_rentedItems == null)
                return;

            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            canvas.Save();
            try
            {
                canvas.ClipRect(Bounds.ToSKRect(), SKClipOperation.Intersect, antialias: false);

                using SKPaint paint = new()
                {
                    IsAntialias = true
                };

                for (int i = 0; i < _itemCount; i++)
                {
                    DrawItem item = _rentedItems[i];
                    SKRect destination = item.Bounds.ToSKRect();
                    paint.Color = new SKColor(255, 255, 255, (byte)Math.Round(item.Opacity * 255));

                    if (item.Image != null)
                    {
                        canvas.DrawImage(item.Image.Image, destination, SamplingOptions, paint);
                    }
                    else
                    {
                        paint.Style = SKPaintStyle.Fill;
                        paint.Color = new SKColor(64, 64, 64, (byte)Math.Round(item.Opacity * 180));
                        canvas.DrawRoundRect(destination, 4, 4, paint);
                        paint.Style = SKPaintStyle.Fill;
                    }
                }
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

            if (_rentedItems != null)
                ArrayPool<DrawItem>.Shared.Return(_rentedItems, clearArray: true);

            _disposed = true;
        }
    }
}