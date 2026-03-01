using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Interactions;
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

    public static readonly StyledProperty<IEnumerable<SignatureSegment>> SignaturesProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IEnumerable<SignatureSegment>>(nameof(Signatures));

    public IEnumerable<SignatureSegment> Signatures
    {
        get => GetValue(SignaturesProperty);
        set => SetValue(SignaturesProperty, value);
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IBrush?>(nameof(Background), Brushes.Transparent);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    // --- Caches & Resources ---

    // NOTE: Pens/Brushes moved to TimelineResources to centralize UI resources.
    // Use TimelineResources.LinePen, TimelineResources.SelectionPen, etc.
    private static readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    // Simple FormattedText cache to avoid recreating layouts repeatedly when rendering many clips
    private readonly Dictionary<(ClipViewModel clip, double fontSize), FormattedText> _textCache = [];

    // Track per-clip handlers so external updates invalidate visuals
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = [];

    // Resize hit threshold (pixels)
    private const double ResizeHitThreshold = 6.0;

    // Track last explicitly selected clip for shift-range selection
    private ClipViewModel? _lastSelectedClip;

    // --- Interaction Handlers ---
    private readonly ClipDragHandler? _dragHandler;
    private readonly ClipResizeHandler? _resizeHandler;
    private readonly BoxSelectionHandler? _boxSelectionHandler;

    static TimelineTrackPanel()
    {
        AffectsRender<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty,
            BackgroundProperty,
            SignaturesProperty);

        AffectsMeasure<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty);

        ClipsProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.OnClipsChanged(e));
    }

    public TimelineTrackPanel()
    {
        // Initialize interaction handlers
        _dragHandler = new ClipDragHandler(this);
        _resizeHandler = new ClipResizeHandler(this);
        _boxSelectionHandler = new BoxSelectionHandler(this);

        // Allow external drag/drop (library -> timeline)
        DragDrop.SetAllowDrop(this, true);

        // Wire drag/drop handlers for external sources (e.g., Library tool)
        AddHandler(DragDrop.DragEnterEvent, OnExternalDragEnter, handledEventsToo: false);
        AddHandler(DragDrop.DragOverEvent, OnExternalDragOver, handledEventsToo: false);
        AddHandler(DragDrop.DragLeaveEvent, OnExternalDragLeave, handledEventsToo: false);
        AddHandler(DragDrop.DropEvent, OnExternalDrop, handledEventsToo: false);
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

    /// <summary>
    /// Helper method to find the parent TimelineEditorViewModel for this panel.
    /// </summary>
    private TimelineEditorViewModel? GetTimelineVM()
    {
        Visual? visualParent = this.GetVisualParent();
        while (visualParent != null)
        {
            if (visualParent is Control c && c.DataContext is TimelineEditorViewModel t)
            {
                return t;
            }

            visualParent = visualParent.GetVisualParent();
        }

        return null;
    }

    private void OnClipsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged oldObs)
        {
            oldObs.CollectionChanged -= OnCollectionChanged;

            // remove handlers for previous collection
            if (e.OldValue is IEnumerable<ClipViewModel> oldClips)
            {
                foreach (ClipViewModel c in oldClips)
                    RemoveClipHandler(c);
            }
        }

        if (e.NewValue is INotifyCollectionChanged newObs)
        {
            newObs.CollectionChanged += OnCollectionChanged;

            // subscribe handlers for new collection
            if (e.NewValue is IEnumerable<ClipViewModel> newClips)
            {
                foreach (ClipViewModel c in newClips)
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
            foreach (object? item in e.NewItems)
            {
                if (item is ClipViewModel clip)
                    AddClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
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
        if (clip == null)
            return;
        if (_clipHandlers.ContainsKey(clip))
            return;

        void handler(object? s, PropertyChangedEventArgs e)
        {
            // clear text cache for this clip
            List<(ClipViewModel clip, double fontSize)> keys = [.. _textCache.Keys.Where(k => k.clip == clip)];
            foreach ((ClipViewModel clip, double fontSize) k in keys)
                _textCache.Remove(k);

            // Ensure arrange/render happens on UI thread
            Dispatcher.UIThread.Post(() =>
            {
                InvalidateMeasure();
                InvalidateVisual();
            });
        }

        clip.PropertyChanged += handler;
        _clipHandlers[clip] = handler;
    }

    private void RemoveClipHandler(ClipViewModel clip)
    {
        if (clip == null)
            return;
        if (_clipHandlers.TryGetValue(clip, out PropertyChangedEventHandler? handler))
        {
            clip.PropertyChanged -= handler;
            _clipHandlers.Remove(clip);
        }
    }

    // Helper to get or create cached FormattedText
    private FormattedText GetFormattedText(ClipViewModel clip, string text, double fontSize, double maxWidth, double maxHeight)
    {
        (ClipViewModel clip, double fontSize) key = (clip, fontSize);
        if (_textCache.TryGetValue(key, out FormattedText? ft))
            return ft;

        ft = new FormattedText(
            text,
            _culture,
            FlowDirection.LeftToRight,
            TimelineResources.DefaultTypeface,
            fontSize,
            TimelineResources.ClipLabelBrush)
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
            foreach (ClipViewModel clip in Clips)
            {
                double endX = (clip.StartBeat - BeatOffset + clip.DurationBeats) * PixelsPerBeat;
                if (endX > width)
                    width = endX;
            }
        }

        return new Size(Math.Max(0, width), availableSize.Height);
    }

    // --- Rendering ---

    // Render moved to partial class TimelineTrackPanel.Render.cs

    // --- Interaction ---

    // Interaction handlers moved to partial class TimelineTrackPanel.Input.cs
}