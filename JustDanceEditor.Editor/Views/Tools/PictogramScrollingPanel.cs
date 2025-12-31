using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Tools;

public class PictogramScrollingPanel : Panel
{
    public static readonly StyledProperty<double> CurrentBeatProperty =
        AvaloniaProperty.Register<PictogramScrollingPanel, double>(nameof(CurrentBeat));

    public double CurrentBeat
    {
        get => GetValue(CurrentBeatProperty);
        set => SetValue(CurrentBeatProperty, value);
    }

    public static readonly StyledProperty<TimelineEditorViewModel?> ActiveTimelineProperty =
        AvaloniaProperty.Register<PictogramScrollingPanel, TimelineEditorViewModel?>(nameof(ActiveTimeline));

    public TimelineEditorViewModel? ActiveTimeline
    {
        get => GetValue(ActiveTimelineProperty);
        set => SetValue(ActiveTimelineProperty, value);
    }

    static PictogramScrollingPanel()
    {
        // Whenever CurrentBeat changes, we need to re-arrange
        AffectsArrange<PictogramScrollingPanel>(CurrentBeatProperty, ActiveTimelineProperty);

        // react on active timeline changes immediately
        ActiveTimelineProperty.Changed.AddClassHandler<PictogramScrollingPanel>((x, e) => x.OnActiveTimelineChanged(e));
    }

    private void OnActiveTimelineChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // Ensure subscriptions are updated and arrange is triggered
        EnsureActiveTimelineSubscriptions();
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateArrange);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    // Track subscriptions so we can unsubscribe when clips are removed
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _subscriptions = [];

    // Global subscriptions for pictogram track (to catch moves made elsewhere)
    private TrackViewModel? _pictoTrack;
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _globalClipHandlers = [];

    // Drag state
    private ClipViewModel? _draggingClip;
    private Control? _draggingChild;
    private double _dragOffsetInChildX;

    // Last arrange metrics used for mapping X -> beat while dragging
    private Size _lastArrangeSize = new(0, 0);
    private double _lastScrollDuration;

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Ensure we are subscribed to clip property changes so we can react to drag updates
        ManageSubscriptions();
        EnsureActiveTimelineSubscriptions();

        if (ActiveTimeline == null)
            return finalSize;

        TimelineStructureDocument ts = ActiveTimeline.TimelineStructure;
        double currentTime = CurrentBeat;
        int coachCount = ActiveTimeline.CoachCount;

        double scrollDuration = GetScrollDurationInBeats(currentTime, ts);
        if (scrollDuration <= 0)
            return finalSize;

        // Micro-optimizations: cache children locally and avoid LINQ in tight loop
        Controls children = Children;
        for (int i = 0; i < children.Count; ++i)
        {
            Control child = children[i];
            if (child is not Control control)
            {
                child.Arrange(new Rect(0,0,0,0));
                continue;
            }

            if (control.DataContext is not ClipViewModel clip)
            {
                child.Arrange(new Rect(0,0,0,0));
                continue;
            }

            double startBeat = clip.StartBeat;
            double ppb = GetBeatsPerPixel(coachCount, startBeat, ts);
            double expectedWidth = GetPictoExpectedWidth(coachCount);
            double stopBeat = startBeat + (ppb * expectedWidth);

            // Calculation
            double relPos = (startBeat - currentTime) / scrollDuration;
            double relWidth = (stopBeat - startBeat) / scrollDuration;

            double drawX = finalSize.Width * relPos;
            double drawWidth = finalSize.Width * relWidth;
            
            // Aspect ratio handling: try to get it from the control if it's an Image
            double aspect = 1.0;
            if (control is Image img && img.Source != null)
            {
                aspect = img.Source.Size.Height / img.Source.Size.Width;
            }
            
            double drawHeight = drawWidth * aspect;
            double drawY = (finalSize.Height - drawHeight) / 2.0;

            // Handle fading/perspective at the start
            double offScreenLeft = (drawX < 0) ? (-drawX / drawWidth) : 0f;
            double opacity = Math.Max(0, Math.Min(1.0, 1.0 - (1.8 * offScreenLeft)));
            
            if (drawX < 0)
            {
                drawY -= 0.4 * drawHeight * offScreenLeft;
                drawX = 0;
            }

            // Fast path: if fully off-screen skip work
            if (drawX + drawWidth < 0 || drawX > finalSize.Width)
            {
                control.Arrange(new Rect(0,0,0,0));
                control.Opacity = 0;
                continue;
            }

            control.Opacity = opacity;
            
            if (opacity <= 0.01 || drawWidth <= 0 || drawHeight <= 0)
            {
                control.Arrange(new Rect(0, 0, 0, 0));
            }
            else
            {
                control.Arrange(new Rect(drawX, drawY, drawWidth, drawHeight));
                control.Tag = new Rect(drawX, drawY, drawWidth, drawHeight); // set Tag to outline arranged rect
            }
        }

        // store metrics for dragging calculations
        _lastArrangeSize = finalSize;
        _lastScrollDuration = scrollDuration;

        return finalSize;
    }

    private void ManageSubscriptions()
    {
        // Determine current clips from children
        var currentClips = new HashSet<ClipViewModel>(Children.OfType<Control>()
            .Where(c => c.DataContext is ClipViewModel)
            .Select(c => c.DataContext as ClipViewModel)!);

        // Unsubscribe removed clips
        var toRemove = new List<ClipViewModel>();
        foreach (ClipViewModel k in _subscriptions.Keys)
            if (!currentClips.Contains(k))
                toRemove.Add(k);

        foreach (ClipViewModel k in toRemove)
        {
            k.PropertyChanged -= _subscriptions[k];
            _subscriptions.Remove(k);
        }

        // Subscribe new clips
        foreach (ClipViewModel clip in currentClips)
        {
            if (clip == null)
                continue;
            if (_subscriptions.ContainsKey(clip))
                continue;

            PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)))
                {
                    // Ensure arrange happens on UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateArrange);
                }
            };

            clip.PropertyChanged += handler;
            _subscriptions[clip] = handler;
        }
    }

    // Ensure we subscribe to ActiveTimeline's pictogram track and its clips
    private TimelineEditorViewModel? _lastActiveTimeline;

    private void EnsureActiveTimelineSubscriptions()
    {
        if (_lastActiveTimeline == ActiveTimeline)
            return;

        // Unsubscribe previous
        if (_lastActiveTimeline != null)
        {
            UnsubscribePictoTrack();
            _lastActiveTimeline = null;
        }

        _lastActiveTimeline = ActiveTimeline;

        if (_lastActiveTimeline == null)
            return;

        // Find pictogram track by title
        TrackViewModel? pictoTrack = _lastActiveTimeline.Tracks.FirstOrDefault(t => t.Title == "Pictograms");
        if (pictoTrack != null)
            SubscribePictoTrack(pictoTrack);
    }

    private void SubscribePictoTrack(TrackViewModel track)
    {
        UnsubscribePictoTrack();

        _pictoTrack = track;
        _pictoTrack.Clips.CollectionChanged += PictoClips_CollectionChanged;

        // subscribe existing clips
        foreach (ClipViewModel clip in _pictoTrack.Clips)
        {
            AddGlobalClipHandler(clip);
        }
    }

    private void UnsubscribePictoTrack()
    {
        if (_pictoTrack == null)
            return;

        _pictoTrack.Clips.CollectionChanged -= PictoClips_CollectionChanged;

        foreach (KeyValuePair<ClipViewModel, PropertyChangedEventHandler> kv in _globalClipHandlers.ToList())
        {
            kv.Key.PropertyChanged -= kv.Value;
            _globalClipHandlers.Remove(kv.Key);
        }

        _pictoTrack = null;
    }

    private void PictoClips_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe to new clips
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ClipViewModel clip)
                    AddGlobalClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ClipViewModel clip)
                    RemoveGlobalClipHandler(clip);
            }
        }

        // Ensure arrange to refresh visuals
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateArrange);
    }

    private void AddGlobalClipHandler(ClipViewModel clip)
    {
        if (clip == null)
            return;
        if (_globalClipHandlers.ContainsKey(clip))
            return;

        PropertyChangedEventHandler handler = (s, e) =>
        {
            if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateArrange);
            }
        };

        clip.PropertyChanged += handler;
        _globalClipHandlers[clip] = handler;
    }

    private void RemoveGlobalClipHandler(ClipViewModel clip)
    {
        if (clip == null)
            return;
        if (_globalClipHandlers.TryGetValue(clip, out PropertyChangedEventHandler? handler))
        {
            clip.PropertyChanged -= handler;
            _globalClipHandlers.Remove(clip);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Cleanup subscriptions
        foreach (KeyValuePair<ClipViewModel, PropertyChangedEventHandler> kv in _subscriptions.ToList())
        {
            kv.Key.PropertyChanged -= kv.Value;
        }

        _subscriptions.Clear();

        UnsubscribePictoTrack();
    }

    private double GetScrollDurationInBeats(double beat, TimelineStructureDocument ts)
    {
        double seconds = ts.GetSecondsAtBeat(beat);
        double futureBeat = ts.GetBeatAtSeconds(seconds + 4.0);
        return futureBeat - beat;
    }

    private double GetBeatsPerPixel(int coachCount, double beat, TimelineStructureDocument ts)
    {
        double duration = GetScrollDurationInBeats(beat, ts);
        double scrollWidthCoords = GetScrollWidthInUaf2DCoords(coachCount);
        double scrollWidthPixels = scrollWidthCoords / 0.4;
        return duration / scrollWidthPixels;
    }

    private int GetScrollWidthInUaf2DCoords(int playerCount)
    {
        return playerCount switch
        {
            1 => 800,
            2 => 900,
            3 => 950,
            4 => 1120,
            6 => 1100,
            _ => 800
        };
    }

    private int GetPictoExpectedWidth(int playerCount)
    {
        return playerCount switch
        {
            1 => 512,
            2 or 3 or 4 => 740,
            6 => 968,
            _ => 512
        };
    }

    // --- Dragging interaction ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point pt = e.GetCurrentPoint(this).Position;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        foreach (Control child in Children.OfType<Control>())
        {
            if (child.Bounds.Contains(pt) && child.DataContext is ClipViewModel clip)
            {
                _draggingClip = clip;
                _draggingChild = child;
                _dragOffsetInChildX = pt.X - child.Bounds.X;
                try
                {
                    e.Pointer.Capture(this);
                }
                catch { }

                e.Handled = true;
                break;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggingClip == null || _draggingChild == null)
            return;

        Point pt = e.GetCurrentPoint(this).Position;

        if (_lastArrangeSize.Width <= 0 || _lastScrollDuration <= 0)
            return;

        double newDrawX = pt.X - _dragOffsetInChildX;
        // clamp inside panel
        newDrawX = Math.Max(0, Math.Min(newDrawX, _lastArrangeSize.Width - _draggingChild.Bounds.Width));

        double newStart = CurrentBeat + (newDrawX / _lastArrangeSize.Width * _lastScrollDuration);
        if (newStart < 0)
            newStart = 0;

        // Try to apply snapping if the timeline VM exposes options
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

        if (vm != null)
        {
            if (vm.SnapToGrid)
            {
                newStart = Math.Round(newStart);
            }

            if (vm.SnapToCurrentTimeMarker)
            {
                double current = vm.CurrentBeat;
                if (Math.Abs(newStart - current) <= 0.25)
                    newStart = current;
            }
        }

        _draggingClip.StartBeat = newStart;

        // request immediate rearrange for responsive feedback
        InvalidateArrange();

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggingClip != null)
        {
            _draggingClip = null;
            _draggingChild = null;
            _dragOffsetInChildX = 0;
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

        _draggingClip = null;
        _draggingChild = null;
        _dragOffsetInChildX = 0;
    }
}
