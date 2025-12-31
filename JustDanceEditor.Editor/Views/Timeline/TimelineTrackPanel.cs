using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline;

public partial class TimelineTrackPanel : Control
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

    // Simple FormattedText cache to avoid recreating layouts repeatedly when rendering many clips
    private readonly Dictionary<(ClipViewModel clip, double fontSize), FormattedText> _textCache = new();

    // Track per-clip handlers so external updates invalidate visuals
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = new();

    // Dragging state
    private ClipViewModel? _draggingClip;
    private double _dragStartPointerX;
    private double _dragOriginalStartBeat;
    private bool _isDragging;

    // Resize state
    private bool _isResizingLeft;
    private bool _isResizingRight;
    private double _resizeStartPointerX;
    private double _resizeOriginalStart;
    private double _resizeOriginalDuration;
    private const double ResizeHitThreshold = 6.0; // pixels

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

    // Render moved to partial class TimelineTrackPanel.Render.cs

    // --- Interaction ---

    // Interaction handlers moved to partial class TimelineTrackPanel.Input.cs
}