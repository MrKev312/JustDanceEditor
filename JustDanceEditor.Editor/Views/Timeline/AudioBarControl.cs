using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using KevInc.Avalonia.Timeline;

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace JustDanceEditor.Editor.Views.Timeline;

public class AudioBarControl : ThemedTimelineControl
{
    private const double ViewportRenderPadding = 64;

    public static readonly StyledProperty<float[]> SamplesProperty =
        AvaloniaProperty.Register<AudioBarControl, float[]>(nameof(Samples));

    public float[] Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(PixelsPerBeat), 50.0);

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public static readonly StyledProperty<int> BeatOffsetProperty =
        AvaloniaProperty.Register<AudioBarControl, int>(nameof(BeatOffset), 0);

    public int BeatOffset
    {
        get => GetValue(BeatOffsetProperty);
        set => SetValue(BeatOffsetProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<SectionSegment>> SectionsProperty =
        AvaloniaProperty.Register<AudioBarControl, IEnumerable<SectionSegment>>(nameof(Sections));

    public IEnumerable<SectionSegment> Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<SignatureSegment>> SignaturesProperty =
        AvaloniaProperty.Register<AudioBarControl, IEnumerable<SignatureSegment>>(nameof(Signatures));

    public IEnumerable<SignatureSegment> Signatures
    {
        get => GetValue(SignaturesProperty);
        set => SetValue(SignaturesProperty, value);
    }

    public static readonly StyledProperty<double> AudioStartBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(AudioStartBeat), double.NaN);

    public double AudioStartBeat
    {
        get => GetValue(AudioStartBeatProperty);
        set => SetValue(AudioStartBeatProperty, value);
    }

    public static readonly StyledProperty<double> AudioEndBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(AudioEndBeat), double.NaN);

    public double AudioEndBeat
    {
        get => GetValue(AudioEndBeatProperty);
        set => SetValue(AudioEndBeatProperty, value);
    }

    private readonly AudioBarRenderer _renderer = new();
    private readonly AudioBarTimelineCache _timelineCache = new();
    private readonly AudioBarInteractionController _interaction;
    private ScrollViewer? _parentScrollViewer;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;
    private TimelineEditorViewModel? _subscribedVm;

    internal List<(Rect rect, SectionSegment section)> SectionLabelRects { get; } = [];
    internal List<(Rect rect, SignatureSegment sig)> SignatureLabelRects { get; } = [];

    static AudioBarControl()
    {
        AffectsRender<AudioBarControl>(
            SamplesProperty,
            PixelsPerBeatProperty,
            SectionsProperty,
            BeatOffsetProperty,
            SignaturesProperty,
            AudioStartBeatProperty,
            AudioEndBeatProperty);
    }

    public AudioBarControl()
    {
        _interaction = new AudioBarInteractionController(this);
        AddHandler(ContextRequestedEvent, _interaction.OnContextRequested);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _parentScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_parentScrollViewer != null)
        {
            _scrollChangedHandler = (_, _) => InvalidateVisual();
            _parentScrollViewer.ScrollChanged += _scrollChangedHandler;
        }

        SubscribeToViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromViewModel();

        if (_parentScrollViewer != null && _scrollChangedHandler != null)
        {
            _parentScrollViewer.ScrollChanged -= _scrollChangedHandler;
            _parentScrollViewer = null;
            _scrollChangedHandler = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnsubscribeFromViewModel();
        SubscribeToViewModel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SectionsProperty)
            _timelineCache.MarkSectionsDirty();
        else if (change.Property == SignaturesProperty)
            _timelineCache.MarkSignaturesDirty();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _interaction.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _interaction.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _interaction.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _interaction.OnPointerCaptureLost();
    }

    public override void Render(DrawingContext context)
    {
        (double visiblePixelStart, double visiblePixelEnd) = GetVisiblePixelRange(Bounds.Width);
        _renderer.Render(new AudioBarRenderRequest
        {
            Context = context,
            Bounds = Bounds,
            PixelsPerBeat = PixelsPerBeat,
            BeatOffset = BeatOffset,
            Samples = Samples,
            AudioStartBeat = AudioStartBeat,
            AudioEndBeat = AudioEndBeat,
            TimelineStructure = _subscribedVm?.TimelineStructure,
            VisiblePixelStart = visiblePixelStart,
            VisiblePixelEnd = visiblePixelEnd,
            SortedSections = _timelineCache.GetSortedSections(Sections),
            SortedSignatures = _timelineCache.GetSortedSignatures(Signatures),
            SectionStarts = _timelineCache.GetSectionStarts(Sections),
            SectionLabelRects = SectionLabelRects,
            SignatureLabelRects = SignatureLabelRects,
            HoveredSection = _interaction.HoveredSection,
            TooltipText = _interaction.TooltipText,
            TooltipPosition = _interaction.TooltipPosition,
            IsDragging = _interaction.IsDragging,
            IsScrubbing = _interaction.IsScrubbing
        });
    }

    private void SubscribeToViewModel()
    {
        if (DataContext is TimelineEditorViewModel vm && vm != _subscribedVm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnsubscribeFromViewModel()
    {
        _subscribedVm?.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedVm = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimelineEditorViewModel.TimelineStructure))
            return;

        _timelineCache.MarkSectionsDirty();
        _timelineCache.MarkSignaturesDirty();
        _renderer.InvalidateTimelineStructure();
        InvalidateVisual();
    }

    private (double Start, double End) GetVisiblePixelRange(double totalWidth)
    {
        double start = 0;
        double end = totalWidth;

        if (_parentScrollViewer != null && _parentScrollViewer.Viewport.Width > 0)
        {
            start = Math.Max(0, _parentScrollViewer.Offset.X - ViewportRenderPadding);
            end = Math.Min(totalWidth, _parentScrollViewer.Offset.X + _parentScrollViewer.Viewport.Width + ViewportRenderPadding);
        }

        return (start, Math.Max(start, end));
    }
}
