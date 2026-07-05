using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using SkiaSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class SkiaGameplayHudControl : Control
{
    private const double BaseWidth = 1920;
    private const double BaseHeight = 1080;

    private static readonly Rect CurrentLyricBaseBounds = new(48, 892, 860, 74);
    private static readonly Rect NextLyricBaseBounds = new(48, 974, 860, 50);
    private static readonly Rect PictogramBaseBounds = new(1095, 808, 825, 220);

    public static readonly StyledProperty<double> CurrentBeatProperty =
        AvaloniaProperty.Register<SkiaGameplayHudControl, double>(nameof(CurrentBeat));

    public double CurrentBeat
    {
        get => GetValue(CurrentBeatProperty);
        set => SetValue(CurrentBeatProperty, value);
    }

    public static readonly StyledProperty<TimelineEditorViewModel?> ActiveTimelineProperty =
        AvaloniaProperty.Register<SkiaGameplayHudControl, TimelineEditorViewModel?>(nameof(ActiveTimeline));

    public TimelineEditorViewModel? ActiveTimeline
    {
        get => GetValue(ActiveTimelineProperty);
        set => SetValue(ActiveTimelineProperty, value);
    }

    public static readonly StyledProperty<LyricLineViewModel?> CurrentLineProperty =
        AvaloniaProperty.Register<SkiaGameplayHudControl, LyricLineViewModel?>(nameof(CurrentLine));

    public LyricLineViewModel? CurrentLine
    {
        get => GetValue(CurrentLineProperty);
        set => SetValue(CurrentLineProperty, value);
    }

    public static readonly StyledProperty<LyricLineViewModel?> NextLineProperty =
        AvaloniaProperty.Register<SkiaGameplayHudControl, LyricLineViewModel?>(nameof(NextLine));

    public LyricLineViewModel? NextLine
    {
        get => GetValue(NextLineProperty);
        set => SetValue(NextLineProperty, value);
    }

    public static readonly StyledProperty<Color> TargetColorProperty =
        AvaloniaProperty.Register<SkiaGameplayHudControl, Color>(nameof(TargetColor), Colors.SkyBlue);

    public Color TargetColor
    {
        get => GetValue(TargetColorProperty);
        set => SetValue(TargetColorProperty, value);
    }

    public static readonly StyledProperty<double> HudOpacityProperty =
        AvaloniaProperty.Register<SkiaGameplayHudControl, double>(nameof(HudOpacity), 1.0);

    public double HudOpacity
    {
        get => GetValue(HudOpacityProperty);
        set => SetValue(HudOpacityProperty, value);
    }

    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = [];
    private readonly List<PictogramClipViewModel> _sortedPictograms = [];
    private readonly List<DrawItem> _drawItems = [];
    private TrackViewModel? _pictogramTrack;
    private TimelineEditorViewModel? _subscribedTimeline;
    private bool _pictogramCacheDirty = true;
    private SkiaLyricLineLayout? _currentLyricLayout;
    private SkiaLyricLineLayout? _nextLyricLayout;

    static SkiaGameplayHudControl()
    {
        AffectsRender<SkiaGameplayHudControl>(
            CurrentBeatProperty,
            ActiveTimelineProperty,
            CurrentLineProperty,
            NextLineProperty,
            TargetColorProperty,
            HudOpacityProperty);

        ActiveTimelineProperty.Changed.AddClassHandler<SkiaGameplayHudControl>((control, _) => control.OnActiveTimelineChanged());
        CurrentLineProperty.Changed.AddClassHandler<SkiaGameplayHudControl>((control, _) => control.ResetCurrentLyricLayout());
        NextLineProperty.Changed.AddClassHandler<SkiaGameplayHudControl>((control, _) => control.ResetNextLyricLayout());
    }

    private void OnActiveTimelineChanged()
    {
        EnsureTimelineSubscription();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Size size = Bounds.Size;
        double hudOpacity = Math.Clamp(HudOpacity, 0, 1);
        if (size.Width <= 1 || size.Height <= 1 || hudOpacity <= 0.001)
            return;

        BuildPictogramDrawItems(size);

        Rect currentLyricBounds = ScaleRect(CurrentLyricBaseBounds, size);
        Rect nextLyricBounds = ScaleRect(NextLyricBaseBounds, size);
        SkiaLyricLineLayout? currentLyricLayout = EnsureLyricLayout(ref _currentLyricLayout, CurrentLine, currentLyricBounds);
        SkiaLyricLineLayout? nextLyricLayout = EnsureLyricLayout(ref _nextLyricLayout, NextLine, nextLyricBounds);

        context.Custom(SkiaGameplayHudDrawOperation.Create(
            new Rect(size),
            _drawItems,
            currentLyricLayout,
            nextLyricLayout,
            CurrentBeat,
            TargetColor,
            hudOpacity));
    }

    private SkiaLyricLineLayout? EnsureLyricLayout(ref SkiaLyricLineLayout? layout, LyricLineViewModel? line, Rect bounds)
    {
        if (line == null || line.Clips.Count == 0)
        {
            layout?.Dispose();
            layout = null;
            return null;
        }

        if (layout?.Matches(line, bounds, isTextLeftAligned: true) == true)
            return layout;

        layout?.Dispose();
        layout = SkiaLyricLineLayout.Create(line, bounds, isTextLeftAligned: true);
        return layout;
    }

    private void BuildPictogramDrawItems(Size size)
    {
        EnsureTimelineSubscription();

        _drawItems.Clear();

        TimelineEditorViewModel? timeline = ActiveTimeline;
        TrackViewModel? track = _pictogramTrack;
        if (timeline == null || track == null)
            return;

        EnsurePictogramCache();

        TimelineStructureDocument timelineStructure = timeline.TimelineStructure;
        double currentBeat = CurrentBeat;
        double scrollDuration = GetScrollDurationInBeats(currentBeat, timelineStructure);
        if (scrollDuration <= 0)
            return;

        Rect pictogramBounds = ScaleRect(PictogramBaseBounds, size);
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
            double beatsPerPixel = GetBeatsPerPixel(coachCount, startBeat, timelineStructure);
            double expectedWidth = GetPictogramExpectedWidth(coachCount);
            double stopBeat = startBeat + beatsPerPixel * expectedWidth;

            double relativeDrawX = pictogramBounds.Width * ((startBeat - currentBeat) / scrollDuration);
            double drawWidth = pictogramBounds.Width * ((stopBeat - startBeat) / scrollDuration);
            if (drawWidth <= 0 || relativeDrawX > pictogramBounds.Width || relativeDrawX + drawWidth < 0)
                continue;

            SkiaPictogramImage? image = null;
            if (!SkiaPictogramImageCache.TryGet(pictogram.ImagePath, out image))
                SkiaPictogramImageCache.ScheduleLoad(pictogram.ImagePath, InvalidateVisual);

            double aspect = image != null && image.Width > 0
                ? image.Height / (double)image.Width
                : defaultAspect;

            double drawHeight = drawWidth * aspect;
            double drawX = pictogramBounds.X + relativeDrawX;
            double drawY = pictogramBounds.Y + (pictogramBounds.Height - drawHeight) / 2.0;

            double offScreenLeft = relativeDrawX < 0 ? -relativeDrawX / drawWidth : 0;
            double opacity = Math.Clamp(1.0 - 1.8 * offScreenLeft, 0, 1);
            if (relativeDrawX < 0)
            {
                drawY -= 0.4 * drawHeight * offScreenLeft;
                drawX = pictogramBounds.X;
            }

            if (opacity <= 0.01 || drawHeight <= 0)
                continue;

            _drawItems.Add(new DrawItem(image, new Rect(drawX, drawY, drawWidth, drawHeight), opacity));
        }
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
        UnsubscribeTrack();
        _subscribedTimeline = null;
        ResetCurrentLyricLayout();
        ResetNextLyricLayout();
    }

    private void ResetCurrentLyricLayout()
    {
        _currentLyricLayout?.Dispose();
        _currentLyricLayout = null;
    }

    private void ResetNextLyricLayout()
    {
        _nextLyricLayout?.Dispose();
        _nextLyricLayout = null;
    }

    private static Rect ScaleRect(Rect rect, Size size)
    {
        double scaleX = size.Width / BaseWidth;
        double scaleY = size.Height / BaseHeight;
        return new Rect(rect.X * scaleX, rect.Y * scaleY, rect.Width * scaleX, rect.Height * scaleY);
    }

    private static double GetScrollDurationInBeats(double beat, TimelineStructureDocument timelineStructure)
    {
        double seconds = timelineStructure.GetSecondsAtBeat(beat);
        double futureBeat = timelineStructure.GetBeatAtSeconds(seconds + 4.0);
        return futureBeat - beat;
    }

    private static double GetBeatsPerPixel(int coachCount, double beat, TimelineStructureDocument timelineStructure)
    {
        double duration = GetScrollDurationInBeats(beat, timelineStructure);
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

    private sealed class SkiaGameplayHudDrawOperation : ICustomDrawOperation
    {
        private static readonly SKSamplingOptions SamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        private readonly DrawItem[]? _rentedItems;
        private readonly int _itemCount;
        private readonly SkiaLyricLineLayout? _currentLyricLayout;
        private readonly SkiaLyricLineLayout? _nextLyricLayout;
        private readonly double _currentBeat;
        private readonly Color _targetColor;
        private readonly double _hudOpacity;
        private bool _disposed;

        private SkiaGameplayHudDrawOperation(
            Rect bounds,
            DrawItem[]? rentedItems,
            int itemCount,
            SkiaLyricLineLayout? currentLyricLayout,
            SkiaLyricLineLayout? nextLyricLayout,
            double currentBeat,
            Color targetColor,
            double hudOpacity)
        {
            Bounds = bounds;
            _rentedItems = rentedItems;
            _itemCount = itemCount;
            _currentLyricLayout = currentLyricLayout?.AddReference();
            _nextLyricLayout = nextLyricLayout?.AddReference();
            _currentBeat = currentBeat;
            _targetColor = targetColor;
            _hudOpacity = hudOpacity;
        }

        public Rect Bounds { get; }

        public static SkiaGameplayHudDrawOperation Create(
            Rect bounds,
            List<DrawItem> items,
            SkiaLyricLineLayout? currentLyricLayout,
            SkiaLyricLineLayout? nextLyricLayout,
            double currentBeat,
            Color targetColor,
            double hudOpacity)
        {
            int itemCount = items.Count;
            DrawItem[]? rentedItems = null;
            if (itemCount > 0)
            {
                rentedItems = ArrayPool<DrawItem>.Shared.Rent(itemCount);
                CollectionsMarshal.AsSpan(items).CopyTo(rentedItems);
            }

            return new SkiaGameplayHudDrawOperation(bounds, rentedItems, itemCount, currentLyricLayout, nextLyricLayout, currentBeat, targetColor, hudOpacity);
        }

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
                RenderPictograms(canvas);
                SkiaLyricRenderer.Render(canvas, _currentLyricLayout, _currentBeat, _targetColor, _hudOpacity);
                SkiaLyricRenderer.Render(canvas, _nextLyricLayout, -1, Colors.White, _hudOpacity);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private void RenderPictograms(SKCanvas canvas)
        {
            if (_rentedItems == null)
                return;

            using SKPaint paint = new()
            {
                IsAntialias = true
            };

            for (int i = 0; i < _itemCount; i++)
            {
                DrawItem item = _rentedItems[i];
                SKRect destination = item.Bounds.ToSKRect();
                byte alpha = (byte)Math.Clamp(Math.Round(item.Opacity * _hudOpacity * 255), 0, 255);
                paint.Color = new SKColor(255, 255, 255, alpha);

                if (item.Image != null)
                {
                    canvas.DrawImage(item.Image.Image, destination, SamplingOptions, paint);
                }
                else
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = new SKColor(64, 64, 64, (byte)Math.Clamp(Math.Round(item.Opacity * _hudOpacity * 180), 0, 255));
                    canvas.DrawRoundRect(destination, 4, 4, paint);
                    paint.Style = SKPaintStyle.Fill;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_rentedItems != null)
                ArrayPool<DrawItem>.Shared.Return(_rentedItems, clearArray: true);

            _currentLyricLayout?.Dispose();
            _nextLyricLayout?.Dispose();

            _disposed = true;
        }
    }
}
