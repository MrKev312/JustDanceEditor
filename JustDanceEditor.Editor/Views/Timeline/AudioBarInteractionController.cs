using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Linq;

using TimelineResources = KevInc.Avalonia.Timeline.TimelineResources;

namespace JustDanceEditor.Editor.Views.Timeline;

internal sealed class AudioBarInteractionController(AudioBarControl owner)
{
    private enum DragTarget { None, Section, Signature }

    private DragTarget _dragTarget = DragTarget.None;
    private SectionSegment? _draggedSection;
    private SignatureSegment? _draggedSignature;
    private double _dragOriginalBeat;

    public bool IsDragging { get; private set; }
    public bool IsScrubbing { get; private set; }
    public SectionSegment? HoveredSection { get; private set; }
    public FormattedText? TooltipText { get; private set; }
    public Point TooltipPosition { get; private set; }

    public void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(owner);
        double ppb = owner.PixelsPerBeat;
        double offset = owner.BeatOffset;

        if (point.Properties.IsLeftButtonPressed && TryBeginLabelDrag(point.Position, e))
            return;

        if (e.ClickCount == 2 && owner.Sections != null)
        {
            double clickedBeat = (point.Position.X / ppb) + offset;
            SectionSegment? section = owner.Sections.OrderByDescending(s => s.StartBeat)
                .FirstOrDefault(s => s.StartBeat <= clickedBeat);

            if (section != null && owner.DataContext is TimelineEditorViewModel vm)
            {
                vm.Playback.SeekToBeat(section.StartBeat);
                e.Handled = true;
                return;
            }
        }

        if (point.Properties.IsLeftButtonPressed && owner.DataContext is TimelineEditorViewModel vm2)
        {
            IsScrubbing = true;
            ClearTooltip();
            TryCapture(e.Pointer, owner);
            SeekAtPointer(point.Position.X, vm2);
            e.Handled = true;
        }
    }

    public void OnPointerMoved(PointerEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(owner);
        Point position = point.Position;
        UpdateTooltip(position);

        if (IsDragging)
        {
            DragToPointer(position);
            e.Handled = true;
            return;
        }

        if (!IsScrubbing)
            return;

        if (owner.DataContext is TimelineEditorViewModel scrubVm)
        {
            SeekAtPointer(position.X, scrubVm);
            e.Handled = true;
        }
    }

    public void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (IsDragging)
        {
            CompleteDrag(e);
            return;
        }

        if (!IsScrubbing)
            return;

        IsScrubbing = false;
        TryReleaseCapture(e.Pointer);
        e.Handled = true;
    }

    public void OnPointerCaptureLost()
    {
        IsScrubbing = false;

        if (!IsDragging)
            return;

        if (_dragTarget == DragTarget.Section && _draggedSection != null)
            _draggedSection.StartBeat = _dragOriginalBeat;
        else if (_dragTarget == DragTarget.Signature && _draggedSignature != null)
            _draggedSignature.Marker = _dragOriginalBeat;

        ResetDrag();
        owner.InvalidateVisual();
    }

    public void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (!e.TryGetPosition(owner, out Point position))
            return;

        double beat = Math.Round((position.X / owner.PixelsPerBeat) + owner.BeatOffset);
        if (owner.DataContext is not TimelineEditorViewModel vm)
            return;

        ContextMenu menu = AudioBarContextMenuBuilder.Build(vm, beat, position, owner.SectionLabelRects, owner.SignatureLabelRects);
        if (menu.Items.Count > 0)
        {
            owner.ContextMenu = menu;
            menu.Open(owner);
            e.Handled = true;
        }
    }

    private bool TryBeginLabelDrag(Point position, PointerPressedEventArgs e)
    {
        foreach ((Rect rect, SectionSegment section) in owner.SectionLabelRects)
        {
            if (!rect.Contains(position))
                continue;

            _dragTarget = DragTarget.Section;
            _draggedSection = section;
            _dragOriginalBeat = section.StartBeat;
            IsDragging = true;
            ClearTooltip();
            TryCapture(e.Pointer, owner);
            e.Handled = true;
            return true;
        }

        foreach ((Rect rect, SignatureSegment sig) in owner.SignatureLabelRects)
        {
            if (!rect.Contains(position))
                continue;

            _dragTarget = DragTarget.Signature;
            _draggedSignature = sig;
            _dragOriginalBeat = sig.Marker;
            IsDragging = true;
            ClearTooltip();
            TryCapture(e.Pointer, owner);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void UpdateTooltip(Point position)
    {
        SectionSegment? newHoveredSection = null;
        foreach ((Rect rect, SectionSegment section) in owner.SectionLabelRects)
        {
            if (!rect.Contains(position))
                continue;

            newHoveredSection = section;
            TooltipPosition = new Point(rect.X, rect.Y - 30);
            break;
        }

        if (newHoveredSection == HoveredSection)
            return;

        HoveredSection = newHoveredSection;
        TooltipText = HoveredSection == null
            ? null
            : new FormattedText(
                AudioBarContextMenuBuilder.GetSectionTypeDescription(HoveredSection.SectionType),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                TimelineResources.DefaultTypeface,
                11,
                Brushes.White);
        owner.InvalidateVisual();
    }

    private void DragToPointer(Point position)
    {
        double newBeat = Math.Round((position.X / owner.PixelsPerBeat) + owner.BeatOffset);
        bool changed = false;

        if (_dragTarget == DragTarget.Section && _draggedSection != null && _draggedSection.StartBeat != newBeat)
        {
            _draggedSection.StartBeat = newBeat;
            changed = true;
        }
        else if (_dragTarget == DragTarget.Signature && _draggedSignature != null && _draggedSignature.Marker != newBeat)
        {
            _draggedSignature.Marker = newBeat;
            changed = true;
        }

        if (!changed)
            return;

        if (owner.DataContext is TimelineEditorViewModel vm)
            vm.NotifyStructureChanged();
        else
            owner.InvalidateVisual();
    }

    private void CompleteDrag(PointerReleasedEventArgs e)
    {
        DragTarget target = _dragTarget;
        SectionSegment? draggedSection = _draggedSection;
        SignatureSegment? draggedSignature = _draggedSignature;
        double originalBeat = _dragOriginalBeat;

        ResetDrag();
        TryReleaseCapture(e.Pointer);

        if (owner.DataContext is TimelineEditorViewModel vm)
        {
            if (target == DragTarget.Section && draggedSection != null)
            {
                double finalBeat = draggedSection.StartBeat;
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

        owner.InvalidateVisual();
        e.Handled = true;
    }

    private void ResetDrag()
    {
        IsDragging = false;
        _dragTarget = DragTarget.None;
        _draggedSection = null;
        _draggedSignature = null;
    }

    private void ClearTooltip()
    {
        HoveredSection = null;
        TooltipText = null;
    }

    private static void SeekAtPointer(double x, TimelineEditorViewModel vm)
    {
        double beat = (x / vm.PixelsPerBeat) + vm.BeatOffset;
        beat = SnappingService.FindSnapBeat(beat, vm);
        vm.Playback.SeekToBeat(beat);
    }

    private static void TryCapture(IPointer pointer, IInputElement element)
        => pointer.Capture(element);

    private static void TryReleaseCapture(IPointer pointer)
        => pointer.Capture(null);
}
