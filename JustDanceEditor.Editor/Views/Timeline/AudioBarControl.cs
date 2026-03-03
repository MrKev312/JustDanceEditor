using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views; // for RenderingHelpers
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.Views.Timeline;

public class AudioBarControl : Control
{
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

    /// <summary>Beat label where the audio file begins. NaN = unknown (stretch-to-fill fallback).</summary>
    public static readonly StyledProperty<double> AudioStartBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(AudioStartBeat), double.NaN);

    public double AudioStartBeat
    {
        get => GetValue(AudioStartBeatProperty);
        set => SetValue(AudioStartBeatProperty, value);
    }

    /// <summary>Beat label where the audio file ends. NaN = unknown (stretch-to-fill fallback).</summary>
    public static readonly StyledProperty<double> AudioEndBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(AudioEndBeat), double.NaN);

    public double AudioEndBeat
    {
        get => GetValue(AudioEndBeatProperty);
        set => SetValue(AudioEndBeatProperty, value);
    }

    private static readonly Dictionary<SongSectionType, Color> _sectionColors = [];

    // Cache FormattedText per section type to avoid allocations in render loop
    private readonly Dictionary<SongSectionType, FormattedText> _sectionTextCache = [];
    // Cache FormattedText per signature beats value to avoid allocations in render loop
    private readonly Dictionary<int, FormattedText> _signatureTextCache = [];
    private double _lastPixelsPerBeat = -1;
    private Size _lastBounds = default;

    // Waveform envelope cache: one min/max pair per pixel column
    private float[]? _envelopeMax;
    private float[]? _envelopeMin;
    private int _envelopeCacheWidth;
    private float[]? _envelopeCacheSamples;
    // Beat-space bounds used when the cache was last built
    private double _envelopeCacheAudioStartBeat = double.NaN;
    private double _envelopeCacheAudioEndBeat = double.NaN;
    private double _envelopeCacheBeatOffset = double.NaN;
    private double _envelopeCachePpb = double.NaN;

    // scrubbing state
    private bool _isScrubbing = false;
    private ScrollViewer? _parentScrollViewer;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;
    private TimelineEditorViewModel? _subscribedVm;

    // drag state for section/signature labels
    private enum DragTarget { None, Section, Signature }
    private DragTarget _dragTarget = DragTarget.None;
    private SectionSegment? _draggedSection;
    private SignatureSegment? _draggedSignature;
    private double _dragOriginalBeat;
    private bool _isDragging;

    // Hit-test rectangles built during Render
    private readonly List<(Rect rect, SectionSegment section)> _sectionLabelRects = [];
    private readonly List<(Rect rect, SignatureSegment sig)> _signatureLabelRects = [];

    // Tooltip tracking: for section labels
    private SectionSegment? _hoveredSection;
    private FormattedText? _tooltipText;
    private Point _tooltipPosition;

    static AudioBarControl()
    {
        AffectsRender<AudioBarControl>(SamplesProperty, PixelsPerBeatProperty, SectionsProperty,
            BeatOffsetProperty, SignaturesProperty, AudioStartBeatProperty, AudioEndBeatProperty);

        // Pre-cache section colors
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
            ColorAttribute? attr = field?.GetCustomAttribute<ColorAttribute>();
            if (attr != null)
            {
                _sectionColors[type] = Color.FromRgb(attr.R, attr.G, attr.B);
            }
            else
            {
                _sectionColors[type] = Colors.Gray;
            }
        }
    }

    // Instance constructor — Avalonia requires a parameterless ctor for custom controls
    public AudioBarControl()
    {
        // Use ContextRequested (routed event, fires on PointerReleased) so that a
        // right-click that dismisses an open popup still opens a fresh menu on
        // the same gesture rather than requiring a second click.
        AddHandler(ContextRequestedEvent, OnContextRequested);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Find parent ScrollViewer and attach scroll listener for viewport changes
        _parentScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_parentScrollViewer != null)
        {
            _scrollChangedHandler = (s, ev) => InvalidateVisual();
            _parentScrollViewer.ScrollChanged += _scrollChangedHandler;
        }

        // Subscribe to ViewModel PropertyChanged so we re-render when TimelineStructure changes
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
        if (e.PropertyName == nameof(TimelineEditorViewModel.TimelineStructure))
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPoint point = e.GetCurrentPoint(this);
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // Check if pointer is over a section or signature label → start drag
        if (point.Properties.IsLeftButtonPressed)
        {
            Point pos = point.Position;

            // Check section labels first (top)
            foreach ((Rect rect, SectionSegment section) in _sectionLabelRects)
            {
                if (rect.Contains(pos))
                {
                    _dragTarget = DragTarget.Section;
                    _draggedSection = section;
                    _dragOriginalBeat = section.StartBeat;
                    _isDragging = true;
                    // Clear tooltip when drag starts
                    _hoveredSection = null;
                    _tooltipText = null;
                    try
                    {
                        e.Pointer.Capture(this);
                    }
                    catch { }

                    e.Handled = true;
                    return;
                }
            }

            // Check signature labels (bottom)
            foreach ((Rect rect, SignatureSegment sig) in _signatureLabelRects)
            {
                if (rect.Contains(pos))
                {
                    _dragTarget = DragTarget.Signature;
                    _draggedSignature = sig;
                    _dragOriginalBeat = sig.Marker;
                    _isDragging = true;
                    // Clear tooltip when drag starts
                    _hoveredSection = null;
                    _tooltipText = null;
                    try
                    {
                        e.Pointer.Capture(this);
                    }
                    catch { }

                    e.Handled = true;
                    return;
                }
            }
        }

        // Double-click to jump to section (existing behavior)
        if (e.ClickCount == 2 && Sections != null)
        {
            double clickedBeat = (point.Position.X / ppb) + offset;

            // Find the section that contains or starts at this beat
            SectionSegment? section = Sections.OrderByDescending(s => s.StartBeat)
                                 .FirstOrDefault(s => s.StartBeat <= clickedBeat);

            if (section != null && DataContext is TimelineEditorViewModel vm)
            {
                vm.Playback.SeekToBeat(section.StartBeat);
                e.Handled = true;
                return;
            }
        }

        // Start scrubbing on left button
        if (point.Properties.IsLeftButtonPressed && DataContext is TimelineEditorViewModel vm2)
        {
            _isScrubbing = true;
            // Clear tooltip when interaction starts
            _hoveredSection = null;
            _tooltipText = null;
            // capture pointer
            try
            {
                e.Pointer.Capture(this);
            }
            catch { }

            SeekAtPointer(point.Position.X, vm2);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        PointerPoint point = e.GetCurrentPoint(this);
        Point pos = point.Position;

        // Track hover on section labels for tooltip display
        SectionSegment? newHoveredSection = null;
        foreach ((Rect rect, SectionSegment section) in _sectionLabelRects)
        {
            if (rect.Contains(pos))
            {
                newHoveredSection = section;
                _tooltipPosition = new Point(rect.X, rect.Y - 30); // Position tooltip above the label
                break;
            }
        }

        if (newHoveredSection != _hoveredSection)
        {
            _hoveredSection = newHoveredSection;
            if (_hoveredSection != null)
            {
                // Build tooltip text for the hovered section
                string description = GetSectionTypeDescription(_hoveredSection.SectionType);
                _tooltipText = new FormattedText(
                    description,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    TimelineResources.DefaultTypeface,
                    11,
                    Brushes.White);
            }
            else
            {
                _tooltipText = null;
            }

            InvalidateVisual(); // Redraw to show/hide tooltip
        }

        if (_isDragging)
        {
            double ppb = PixelsPerBeat;
            double offset = BeatOffset;
            double newBeat = Math.Round((point.Position.X / ppb) + offset);

            bool changed = false;
            if (_dragTarget == DragTarget.Section && _draggedSection != null)
            {
                if (_draggedSection.StartBeat != newBeat)
                {
                    _draggedSection.StartBeat = newBeat;
                    changed = true;
                }
            }
            else if (_dragTarget == DragTarget.Signature && _draggedSignature != null)
            {
                if (_draggedSignature.Marker != newBeat)
                {
                    _draggedSignature.Marker = newBeat;
                    changed = true;
                }
            }

            if (changed)
            {
                // Notify VM so all panels (tracks, ruler) re-render in real time
                if (DataContext is TimelineEditorViewModel vm)
                    vm.NotifyStructureChanged();
                else
                    InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        if (!_isScrubbing)
            return;
        PointerPoint pt = e.GetCurrentPoint(this);
        if (DataContext is TimelineEditorViewModel scrubVm)
        {
            SeekAtPointer(pt.Position.X, scrubVm);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            // Save drag state BEFORE releasing capture, because Capture(null)
            // fires OnPointerCaptureLost synchronously which would reset everything.
            DragTarget target = _dragTarget;
            SectionSegment? draggedSection = _draggedSection;
            SignatureSegment? draggedSignature = _draggedSignature;
            double originalBeat = _dragOriginalBeat;

            // Clear drag state first so OnPointerCaptureLost doesn't cancel
            _isDragging = false;
            _dragTarget = DragTarget.None;
            _draggedSection = null;
            _draggedSignature = null;

            try
            {
                e.Pointer.Capture(null);
            }
            catch { }

            if (DataContext is TimelineEditorViewModel vm)
            {
                if (target == DragTarget.Section && draggedSection != null)
                {
                    double finalBeat = draggedSection.StartBeat;
                    // Reset to original before calling VM (it will set the new value and record undo)
                    draggedSection.StartBeat = originalBeat;
                    vm.MoveSection(draggedSection, finalBeat);
                }
                else if (target == DragTarget.Signature && draggedSignature != null)
                {
                    double finalBeat = draggedSignature.Marker;
                    draggedSignature.Marker = originalBeat;
                    vm.MoveSignature(draggedSignature, finalBeat);
                }
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!_isScrubbing)
            return;
        _isScrubbing = false;
        try
        {
            e.Pointer.Capture(null);
        }
        catch { }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isScrubbing = false;

        if (_isDragging)
        {
            // Cancel drag - reset to original
            if (_dragTarget == DragTarget.Section && _draggedSection != null)
                _draggedSection.StartBeat = _dragOriginalBeat;
            else if (_dragTarget == DragTarget.Signature && _draggedSignature != null)
                _draggedSignature.Marker = _dragOriginalBeat;

            _isDragging = false;
            _dragTarget = DragTarget.None;
            _draggedSection = null;
            _draggedSignature = null;
            InvalidateVisual();
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // ContextRequested fires on PointerReleased, so any previously open
        // context-menu popup has already been dismissed by the PointerPressed
        // that preceded this release. This avoids the "two clicks to replace
        // a menu" problem that occurs when handling right-click in OnPointerPressed.
        if (!e.TryGetPosition(this, out Point position))
            return;

        double beat = Math.Round((position.X / PixelsPerBeat) + BeatOffset);
        ContextMenu menu = BuildContextMenu(beat, position);
        if (menu.Items.Count > 0)
        {
            ContextMenu = menu;   // assign so Avalonia tracks lifetime
            menu.Open(this);
            e.Handled = true;
        }
    }

    private ContextMenu BuildContextMenu(double beat, Point position)
    {
        if (DataContext is not TimelineEditorViewModel vm)
            return new ContextMenu();

        // Check if right-click is on an existing section or signature label
        SectionSegment? clickedSection = null;
        SignatureSegment? clickedSignature = null;

        foreach ((Rect rect, SectionSegment section) in _sectionLabelRects)
        {
            if (rect.Contains(position))
            {
                clickedSection = section;
                break;
            }
        }

        if (clickedSection == null)
        {
            foreach ((Rect rect, SignatureSegment sig) in _signatureLabelRects)
            {
                if (rect.Contains(position))
                {
                    clickedSignature = sig;
                    break;
                }
            }
        }

        ContextMenu menu = new();

        if (clickedSection != null)
        {
            // Context menu for existing section
            MenuItem changeTypeMenu = new() { Header = "Change Type" };
            foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
            {
                SongSectionType capturedType = type;
                SectionSegment capturedSec = clickedSection;
                MenuItem item = new() { Header = type.ToString() };
                string description = GetSectionTypeDescription(type);
                if (!string.IsNullOrEmpty(description))
                    ToolTip.SetTip(item, description);
                item.Click += (_, _) => vm.ChangeSectionType(capturedSec, capturedType);
                changeTypeMenu.Items.Add(item);
            }

            menu.Items.Add(changeTypeMenu);

            SectionSegment removeTarget = clickedSection;
            MenuItem removeItem = new() { Header = "Remove Section" };
            removeItem.Click += (_, _) => vm.RemoveSection(removeTarget);
            menu.Items.Add(removeItem);
        }
        else if (clickedSignature != null)
        {
            // Context menu for existing signature
            MenuItem changeBeatsMenu = new() { Header = "Change Beats" };
            for (int b = 1; b <= 16; b++)
            {
                int capturedBeats = b;
                SignatureSegment capturedSig = clickedSignature;
                MenuItem item = new() { Header = $"{b}/4" };
                item.Click += (_, _) => vm.ChangeSignatureBeats(capturedSig, capturedBeats);
                changeBeatsMenu.Items.Add(item);
            }

            menu.Items.Add(changeBeatsMenu);

            SignatureSegment removeSigTarget = clickedSignature;
            MenuItem removeSigItem = new() { Header = "Remove Signature" };
            removeSigItem.Click += (_, _) => vm.RemoveSignature(removeSigTarget);
            menu.Items.Add(removeSigItem);
        }
        else
        {
            // Context menu for empty area → add new
            MenuItem addSectionMenu = new() { Header = $"Add Section at Beat {beat}" };
            foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
            {
                SongSectionType capturedType = type;
                double capturedBeat = beat;
                MenuItem item = new() { Header = type.ToString() };
                string description = GetSectionTypeDescription(type);
                if (!string.IsNullOrEmpty(description))
                    ToolTip.SetTip(item, description);
                item.Click += (_, _) => vm.AddSection(capturedBeat, capturedType);
                addSectionMenu.Items.Add(item);
            }

            menu.Items.Add(addSectionMenu);

            MenuItem addSigMenu = new() { Header = $"Add Signature at Beat {beat}" };
            for (int b = 1; b <= 16; b++)
            {
                int capturedBeats = b;
                double capturedBeat = beat;
                MenuItem item = new() { Header = $"{b}/4" };
                item.Click += (_, _) => vm.AddSignature(capturedBeat, capturedBeats);
                addSigMenu.Items.Add(item);
            }

            menu.Items.Add(addSigMenu);
        }

        return menu;
    }

    /// <summary>
    /// Extracts the [Description] attribute value from a SongSectionType enum value.
    /// </summary>
    private static string GetSectionTypeDescription(SongSectionType type)
    {
        FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
        if (field == null)
            return string.Empty;

        DescriptionAttribute? attr = field.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? string.Empty;
    }

    private static void SeekAtPointer(double x, TimelineEditorViewModel vm)
    {
        // Convert local X to beat
        double beat = (x / vm.PixelsPerBeat) + vm.BeatOffset;

        // Apply centralized snapping logic
        beat = SnappingService.FindSnapBeat(beat, vm);

        vm.Playback.SeekToBeat(beat);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // Fill entire bounds with a transparent brush so the whole control
        // participates in Avalonia's hit-testing (required for pointer events
        // to fire on areas with no drawn content).
        context.FillRectangle(Brushes.Transparent, bounds);

        // Clear hit-test rectangles
        _sectionLabelRects.Clear();
        _signatureLabelRects.Clear();

        // 1. Draw Section Backgrounds
        if (Sections != null)
        {
            List<SectionSegment> sortedSections = [.. Sections.OrderBy(s => s.StartBeat)];
            for (int i = 0; i < sortedSections.Count; i++)
            {
                SectionSegment section = sortedSections[i];
                Color color = _sectionColors.TryGetValue(section.SectionType, out Color c) ? c : Colors.Gray;

                double startX = (section.StartBeat - offset) * ppb;
                double endX = bounds.Width;

                if (i + 1 < sortedSections.Count)
                {
                    endX = (sortedSections[i + 1].StartBeat - offset) * ppb;
                }

                if (startX < bounds.Width && endX > 0)
                {
                    SolidColorBrush sectionBrush = new(color, 0.3);
                    Rect rect = new(Math.Max(0, startX), 0, Math.Min(bounds.Width, endX) - Math.Max(0, startX), bounds.Height);
                    context.FillRectangle(sectionBrush, rect);
                }
            }
        }

        // 1.5. Draw Measure Backgrounds (alternating pattern)
        double maxBeat = bounds.Width / Math.Max(1.0, ppb);
        DrawMeasureBackgrounds(context, bounds, ppb, (int)offset, maxBeat);

        // 1.75. Draw Grid Lines
        double visibleStartBeat = offset - 1;
        double visibleEndBeat = offset + (bounds.Width / Math.Max(1.0, ppb)) + 1;
        DrawGridLines(context, bounds, ppb, (int)offset, visibleStartBeat, visibleEndBeat);

        // 2. Draw Waveform
        if (Samples != null && Samples.Length > 0)
        {
            int totalWidth = (int)bounds.Width;
            double centerY = bounds.Height / 2;

            // Resolve audio extent in pixels
            // Fall back to stretching across the full control when bounds are unknown.
            double aStart = AudioStartBeat;
            double aEnd = AudioEndBeat;
            bool boundsKnown = !double.IsNaN(aStart) && !double.IsNaN(aEnd) &&
                               !double.IsInfinity(aStart) && !double.IsInfinity(aEnd) &&
                               aEnd > aStart;

            double audioStartX = boundsKnown ? (aStart - offset) * ppb : 0;
            double audioEndX = boundsKnown ? (aEnd - offset) * ppb : totalWidth;

            // Clamp to control bounds for drawing
            double drawStartX = Math.Max(0, audioStartX);
            double drawEndX = Math.Min(totalWidth, audioEndX);

            // Rebuild envelope cache when anything affecting the pixel→sample mapping changes
            bool cacheStale = _envelopeCacheSamples != Samples
                || _envelopeCacheWidth != totalWidth
                || Math.Abs(_envelopeCacheAudioStartBeat - aStart) > 0.001
                || Math.Abs(_envelopeCacheAudioEndBeat - aEnd) > 0.001
                || Math.Abs(_envelopeCacheBeatOffset - offset) > 0.001
                || Math.Abs(_envelopeCachePpb - ppb) > 0.001;

            if (cacheStale)
            {
                _envelopeCacheWidth = totalWidth;
                _envelopeCacheSamples = Samples;
                _envelopeCacheAudioStartBeat = aStart;
                _envelopeCacheAudioEndBeat = aEnd;
                _envelopeCacheBeatOffset = offset;
                _envelopeCachePpb = ppb;

                _envelopeMax = new float[totalWidth];
                _envelopeMin = new float[totalWidth];

                double audioPxWidth = audioEndX - audioStartX;

                for (int x = 0; x < totalWidth; x++)
                {
                    // Map pixel x into [0,1] within the audio range
                    double t = audioPxWidth > 0 ? (x - audioStartX) / audioPxWidth : -1;
                    if (t is < 0 or >= 1)
                    {
                        _envelopeMax[x] = 0;
                        _envelopeMin[x] = 0;
                        continue;
                    }

                    int startIdx = (int)(t * Samples.Length);
                    int endIdx = (int)((t + (1.0 / audioPxWidth)) * Samples.Length);
                    if (endIdx > Samples.Length)
                        endIdx = Samples.Length;
                    if (startIdx >= endIdx)
                        endIdx = startIdx + 1;
                    if (endIdx > Samples.Length)
                        endIdx = Samples.Length;

                    float maxV = 0, minV = 0;
                    for (int i = startIdx; i < endIdx && i < Samples.Length; i++)
                    {
                        float val = Samples[i];
                        if (val > maxV)
                            maxV = val;
                        if (val < minV)
                            minV = val;
                    }

                    _envelopeMax[x] = maxV;
                    _envelopeMin[x] = minV;
                }
            }

            // Find the visible pixel range from the parent ScrollViewer
            int visibleStartX = 0;
            int visibleEndX = totalWidth;

            ScrollViewer? sv = this.FindAncestorOfType<ScrollViewer>();
            if (sv != null)
            {
                visibleStartX = Math.Max(0, (int)sv.Offset.X);
                visibleEndX = Math.Min(totalWidth, (int)(sv.Offset.X + sv.Viewport.Width));
            }

            // Add 10% buffer on both sides for smooth scrolling
            int bufferSize = Math.Max(1, (visibleEndX - visibleStartX) / 10);
            int renderStartX = Math.Max(0, visibleStartX - bufferSize);
            int renderEndX = Math.Min(totalWidth, visibleEndX + bufferSize);

            // Draw out-of-audio danger stripes (before audio start)
            if (boundsKnown && drawStartX > renderStartX)
                DrawNoAudioStripes(context, new Rect(renderStartX, 0, drawStartX - renderStartX, bounds.Height));

            // Draw waveform columns
            int waveStart = Math.Max(renderStartX, (int)drawStartX);
            int waveEnd = Math.Min(renderEndX, (int)drawEndX);
            for (int x = waveStart; x < waveEnd; x++)
            {
                float maxV = _envelopeMax![x];
                float minV = _envelopeMin![x];

                double topH = maxV * centerY * 0.8;
                double botH = minV * centerY * 0.8;

                double topY = centerY - topH;
                double botY = centerY - botH;
                double height = botY - topY;

                if (height < 0.5)
                    continue;

                // Semi-transparent fill
                Rect columnRect = new(x, topY, 1, height);
                context.FillRectangle(TimelineResources.WaveformFill, columnRect);

                // White edge pixels at top and bottom
                context.DrawLine(TimelineResources.WaveformEdgePen, new Point(x, topY), new Point(x + 1, topY));
                context.DrawLine(TimelineResources.WaveformEdgePen, new Point(x, botY), new Point(x + 1, botY));
            }

            // Draw out-of-audio danger stripes (after audio end)
            if (boundsKnown && drawEndX < renderEndX)
                DrawNoAudioStripes(context, new Rect(drawEndX, 0, renderEndX - drawEndX, bounds.Height));
        }

        // 3. Draw Section Labels
        if (Sections != null)
        {
            List<SectionSegment> sortedSections = [.. Sections.OrderBy(s => s.StartBeat)];

            // Rebuild text cache only when size or pixels-per-beat changes
            if (Math.Abs(_lastPixelsPerBeat - ppb) > 1e-9 || !_lastBounds.Equals(bounds.Size))
            {
                _sectionTextCache.Clear();
                _signatureTextCache.Clear();
                foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
                {
                    FormattedText ft = new(
                        type.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        TimelineResources.DefaultTypeface,
                        10,
                        Brushes.White);
                    _sectionTextCache[type] = ft;
                }

                _lastPixelsPerBeat = ppb;
                _lastBounds = bounds.Size;
            }

            foreach (SectionSegment? section in sortedSections)
            {
                double x = (section.StartBeat - offset) * ppb;
                if (x >= 0 && x < bounds.Width)
                {
                    // Draw vertical line
                    context.DrawLine(TimelineResources.SectionBorderPen, new Point(x, 0), new Point(x, bounds.Height));

                    if (_sectionTextCache.TryGetValue(section.SectionType, out FormattedText? text))
                    {
                        Rect bgRect = new(x + 2, 2, text.Width + 4, text.Height + 2);
                        context.FillRectangle(TimelineResources.SectionBgBrush, bgRect);
                        context.DrawText(text, new Point(x + 4, 3));

                        // Store hit-test rect
                        _sectionLabelRects.Add((bgRect, section));
                    }
                }
            }

            // 4. Draw Signature Labels (bottom of waveform)
            if (Signatures != null)
            {
                List<SignatureSegment> sortedSigs = [.. Signatures.OrderBy(s => s.Marker)];
                foreach (SignatureSegment sig in sortedSigs)
                {
                    double x = (sig.Marker - offset) * ppb;
                    if (x >= 0 && x < bounds.Width)
                    {
                        // Get or create formatted text for this signature
                        if (!_signatureTextCache.TryGetValue(sig.Beats, out FormattedText? sigText))
                        {
                            string sigLabel = $"{sig.Beats}/4";
                            sigText = new FormattedText(
                                sigLabel,
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                TimelineResources.DefaultTypeface,
                                10,
                                Brushes.White);
                            _signatureTextCache[sig.Beats] = sigText;
                        }

                        // Draw at bottom of audio bar (above the waveform bottom edge)
                        double textY = bounds.Height - sigText.Height - 2;
                        Rect bgRect = new(x + 2, textY - 2, sigText.Width + 4, sigText.Height + 2);
                        context.FillRectangle(TimelineResources.SectionBgBrush, bgRect);
                        context.DrawText(sigText, new Point(x + 4, textY));

                        // Store hit-test rect
                        _signatureLabelRects.Add((bgRect, sig));
                    }
                }
            }

            // 5. Draw tooltip for hovered section label (only when not dragging/scrubbing)
            if (_hoveredSection != null && _tooltipText != null && !_isDragging && !_isScrubbing)
            {
                // Tooltip background
                Rect tooltipBgRect = new(
                    _tooltipPosition.X,
                    _tooltipPosition.Y,
                    _tooltipText.Width + 8,
                    _tooltipText.Height + 4);

                // Clamp to bounds so it doesn't go off-screen
                if (tooltipBgRect.X + tooltipBgRect.Width > bounds.Width)
                    tooltipBgRect = tooltipBgRect.WithX(bounds.Width - tooltipBgRect.Width - 4);
                if (tooltipBgRect.X < 0)
                    tooltipBgRect = tooltipBgRect.WithX(4);
                if (tooltipBgRect.Y < 0)
                    tooltipBgRect = tooltipBgRect.WithY(_tooltipPosition.Y + 30);

                // Semi-transparent dark background
                context.FillRectangle(
                    new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
                    tooltipBgRect);

                // Border
                context.DrawRectangle(
                    new Pen(new SolidColorBrush(Colors.White), 1),
                    tooltipBgRect);

                // Text
                context.DrawText(
                    _tooltipText,
                    new Point(tooltipBgRect.X + 4, tooltipBgRect.Y + 2));
            }
        }
    }

    /// <summary>Draws red/white diagonal danger stripes to indicate a region with no audio.</summary>
    private static void DrawNoAudioStripes(DrawingContext context, Rect region)
    {
        // Delegate to generic helper so other features can reuse the pattern.
        RenderingHelpers.DrawDiagonalStripes(
            context,
            region,
            Color.FromArgb(80, 180, 0, 0),
            Color.FromArgb(100, 255, 255, 255));
    }

    private void DrawMeasureBackgrounds(DrawingContext context, Rect bounds, double ppb, int offset, double maxBeat)
    {
        List<SignatureSegment> sortedSigs = Signatures?.OrderBy(s => s.Marker).ToList() ?? [];
        List<double> sectionStarts = Sections != null
            ? [.. Sections.OrderBy(s => s.StartBeat).Select(s => (double)s.StartBeat)]
            : [];

        SolidColorBrush brushA = new(Colors.White, 0.05);
        SolidColorBrush brushB = new(Colors.White, 0.02);
        SolidColorBrush brushErr = new(Colors.Red, 0.08);

        double rangeStart = offset;
        double rangeEnd = offset + maxBeat;

        // Build section intervals (in absolute beat space)
        List<(double start, double end)> intervals = [];
        if (sectionStarts.Count == 0)
        {
            intervals.Add((rangeStart, rangeEnd));
        }
        else
        {
            for (int i = 0; i < sectionStarts.Count; i++)
            {
                double sStart = sectionStarts[i];
                double sEnd = (i + 1 < sectionStarts.Count) ? sectionStarts[i + 1] : rangeEnd;
                intervals.Add((sStart, sEnd));
            }
        }

        int colorIndex = 0;
        for (int si = 0; si < intervals.Count; si++)
        {
            bool isLastSection = si == intervals.Count - 1;
            (double sStart, double sEnd) = intervals[si];

            if (sEnd <= rangeStart)
            {
                // Advance colorIndex for off-screen sections
                colorIndex += TimelineRenderHelper.CountAllGroups(sStart, sEnd, sortedSigs);
                continue;
            }

            if (sStart >= rangeEnd)
                break;

            int sectionColor = colorIndex;
            int groupInSection = 0;
            double pos = sStart;

            while (pos < sEnd - 0.01 && pos < rangeEnd)
            {
                int blockSize = TimelineRenderHelper.GetActiveBlockSize(pos, sortedSigs);
                double gEnd = pos + blockSize;

                // Partial due to section boundary (mid-song) → red; end-of-song → normal
                bool isPartialSectionEnd = gEnd > sEnd + 0.01;
                if (isPartialSectionEnd)
                    gEnd = sEnd;

                // Partial due to signature change → red
                double nextSig = TimelineRenderHelper.GetNextSigChange(pos, sortedSigs);
                bool isPartialSigChange = false;
                if (!isPartialSectionEnd && nextSig < gEnd - 0.01)
                {
                    gEnd = nextSig;
                    isPartialSigChange = true;
                }

                bool isPartial = isPartialSigChange || (isPartialSectionEnd && !isLastSection);

                double xStart = (pos - offset) * ppb;
                double xEnd = (gEnd - offset) * ppb;
                if (xEnd >= 0 && xStart <= bounds.Width)
                {
                    SolidColorBrush brush = isPartial ? brushErr : ((sectionColor + groupInSection) % 2 == 0 ? brushA : brushB);
                    context.FillRectangle(brush, new Rect(xStart, 0, xEnd - xStart, bounds.Height));
                }

                groupInSection++;
                pos = gEnd;
            }

            colorIndex += groupInSection;
        }
    }

    private void DrawGridLines(DrawingContext context, Rect bounds, double ppb, int offset, double visibleStartBeat, double visibleEndBeat)
    {
        List<double> sectionStarts = Sections != null
            ? [.. Sections.OrderBy(s => s.StartBeat).Select(s => (double)s.StartBeat)]
            : [];

        for (int beat = (int)visibleStartBeat; beat <= (int)visibleEndBeat; beat++)
        {
            double x = (beat - offset) * ppb;
            if (x < 0 || x > bounds.Width)
                continue;

            bool isMeasure = TimelineRenderHelper.IsBaseGroupBeat(beat, sectionStarts);
            if (isMeasure)
                context.DrawLine(TimelineResources.MeasureGridPen, new Point(x, 0), new Point(x, bounds.Height));
            else
                context.DrawLine(TimelineResources.BeatGridPen, new Point(x, 0), new Point(x, bounds.Height));
        }
    }
}