using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Timeline.Interactions;
using JustDanceEditor.Formats.JDI.Timelines;
using KevInc.Avalonia.Timeline;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimelineTrackPanel : ThemedTimelineControl
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

    public static readonly StyledProperty<IEnumerable<SectionSegment>> SectionsProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IEnumerable<SectionSegment>>(nameof(Sections));

    public IEnumerable<SectionSegment> Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IBrush?>(nameof(Background), Brushes.Transparent);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    // Track per-clip handlers so external updates invalidate visuals
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = [];

    // --- Interaction Handlers ---
    private readonly ClipDragHandler? _dragHandler;
    private readonly ClipResizeHandler? _resizeHandler;
    private readonly BoxSelectionHandler? _boxSelectionHandler;
    private readonly TimelineExternalDropController _externalDropController;
    private readonly TimelineTrackRenderer _renderer;
    private readonly TimelineTrackInputController _inputController;
    private bool _visualInvalidationPending;
    private bool _measureInvalidationPending;

    static TimelineTrackPanel()
    {
        AffectsRender<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty,
            BackgroundProperty,
            SignaturesProperty,
            SectionsProperty);

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
        _renderer = new(this);
        _externalDropController = new(this, GetTimelineVM);
        _inputController = new(this);

        // Allow external drag/drop (library -> timeline)
        DragDrop.SetAllowDrop(this, true);

        // Wire drag/drop handlers for external sources (e.g., Library tool)
        AddHandler(DragDrop.DragEnterEvent, _externalDropController.OnDragEnter, handledEventsToo: false);
        AddHandler(DragDrop.DragOverEvent, _externalDropController.OnDragOver, handledEventsToo: false);
        AddHandler(DragDrop.DragLeaveEvent, _externalDropController.OnDragLeave, handledEventsToo: false);
        AddHandler(DragDrop.DropEvent, _externalDropController.OnDrop, handledEventsToo: false);
    }

    // Track the subscribed parent VM so we can unsubscribe cleanly
    private TimelineEditorViewModel? _subscribedTimelineVm;
    private ScrollViewer? _parentScrollViewer;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToTimelineVm();
        SubscribeToScrollViewer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromScrollViewer();
        UnsubscribeFromTimelineVm();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToScrollViewer()
    {
        _parentScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_parentScrollViewer == null)
            return;

        _scrollChangedHandler = (_, _) => InvalidateVisual();
        _parentScrollViewer.ScrollChanged += _scrollChangedHandler;
    }

    private void UnsubscribeFromScrollViewer()
    {
        if (_parentScrollViewer != null && _scrollChangedHandler != null)
            _parentScrollViewer.ScrollChanged -= _scrollChangedHandler;

        _parentScrollViewer = null;
        _scrollChangedHandler = null;
    }

    private void SubscribeToTimelineVm()
    {
        TimelineEditorViewModel? vm = GetTimelineVM();
        if (vm != null && vm != _subscribedTimelineVm)
        {
            _subscribedTimelineVm = vm;
            vm.PropertyChanged += OnTimelineVmPropertyChanged;
        }
    }

    private void UnsubscribeFromTimelineVm()
    {
        _subscribedTimelineVm?.PropertyChanged -= OnTimelineVmPropertyChanged;
        _subscribedTimelineVm = null;
    }

    private void OnTimelineVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineEditorViewModel.TimelineStructure))
        {
            _renderer.InvalidateTimelineStructure();
            InvalidateVisual();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PixelsPerBeatProperty)
        {
            // zoom changed -> cached text sizes invalid
            _renderer.InvalidateText();
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == SignaturesProperty)
        {
            _renderer.InvalidateSignatures();
        }
        else if (change.Property == SectionsProperty)
        {
            _renderer.InvalidateSections();
        }
    }

    /// <summary>
    /// Helper method to find the parent TimelineEditorViewModel for this panel.
    /// </summary>
    internal TimelineEditorViewModel? GetTimelineVM()
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
        _renderer.InvalidateClips();

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
        _renderer.InvalidateClips();

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
            bool affectsLayout = e.PropertyName is nameof(ClipViewModel.StartBeat) or nameof(ClipViewModel.DurationBeats);
            if (affectsLayout)
                _renderer.InvalidateClipOrder();

            if (e.PropertyName is nameof(ClipViewModel.Name) or nameof(ClipViewModel.DurationBeats))
                _renderer.InvalidateText(clip);

            QueueInvalidation(affectsLayout);
        }

        clip.PropertyChanged += handler;
        _clipHandlers[clip] = handler;
    }

    private void QueueInvalidation(bool measure)
    {
        TimelineRenderDiagnostics.RecordCount("track.invalidate-request");
        _measureInvalidationPending |= measure;
        if (_visualInvalidationPending)
            return;

        _visualInvalidationPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            bool invalidateMeasure = _measureInvalidationPending;
            _measureInvalidationPending = false;
            _visualInvalidationPending = false;

            if (invalidateMeasure)
                InvalidateMeasure();
            InvalidateVisual();
            TimelineRenderDiagnostics.RecordCount("track.invalidate-flush");
        }, DispatcherPriority.Render);
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

    // --- Layout ---

    protected override Size MeasureOverride(Size availableSize)
    {
        long measureStart = TimelineRenderDiagnostics.Start();
        double width = MaxBeat * PixelsPerBeat;
        int clipCount = 0;

        if (Clips != null)
        {
            foreach (ClipViewModel clip in Clips)
            {
                clipCount++;
                double endX = (clip.StartBeat - BeatOffset + clip.DurationBeats) * PixelsPerBeat;
                if (endX > width)
                    width = endX;
            }
        }

        Size measured = new(Math.Max(0, width), availableSize.Height);
        TimelineRenderDiagnostics.RecordDuration("track.measure", measureStart, clipCount);
        return measured;
    }

    public override void Render(DrawingContext context)
    {
        long renderStart = TimelineRenderDiagnostics.Start();
        try
        {
            _renderer.Render(context);
        }
        finally
        {
            TimelineRenderDiagnostics.RecordDuration("track.render", renderStart);
        }
    }

    public static ContextMenu? CurrentContextMenu { get; internal set; }

    internal BoxSelectionHandler? BoxSelectionHandler => _boxSelectionHandler;
    internal ScrollViewer? ParentScrollViewer => _parentScrollViewer;
    internal ClipDragHandler? DragHandler => _dragHandler;
    internal ClipResizeHandler? ResizeHandler => _resizeHandler;
    internal TimelineExternalDropController ExternalDropController => _externalDropController;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _inputController.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _inputController.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _inputController.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _inputController.OnPointerCaptureLost(e);
    }

    public void OpenAddClipMenu(PointerPressedEventArgs? e) => _inputController.OpenAddClipMenu(e);

}
