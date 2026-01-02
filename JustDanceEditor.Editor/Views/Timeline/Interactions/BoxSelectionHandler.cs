using Avalonia;
using Avalonia.Input;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Timeline.Interactions;

/// <summary>
/// Handles marquee (box) selection in the timeline.
/// </summary>
public class BoxSelectionHandler(TimelineTrackPanel panel) : TimelineInteractionHandler(panel)
{
    private ClipViewModel? _lastSelectedClip;

    private const double MinDragDistance = 4.0;

    public bool IsActive { get; private set; }
    public Point StartPoint { get; private set; }
    public Point CurrentPoint { get; private set; }

    public void StartSelection(Point startPoint, PointerEventArgs e)
    {
        IsActive = true;
        StartPoint = startPoint;
        CurrentPoint = startPoint;

        Capture(e);
        try
        {
            _panel.Focus();
        }
        catch { }

        _panel.Cursor = new Cursor(StandardCursorType.Cross);
        _panel.InvalidateVisual();
    }

    public void UpdateSelection(Point currentPoint)
    {
        CurrentPoint = currentPoint;
        _panel.InvalidateVisual();
    }

    public void Complete(TimelineEditorViewModel? vm, PointerEventArgs? e, bool addToSelection = false)
    {
        IsActive = false;

        Release(e);

        _panel.Cursor = new Cursor(StandardCursorType.Arrow);

        Rect selRect = new(
            Math.Min(StartPoint.X, CurrentPoint.X),
            Math.Min(StartPoint.Y, CurrentPoint.Y),
            Math.Abs(CurrentPoint.X - StartPoint.X),
            Math.Abs(CurrentPoint.Y - StartPoint.Y)
        );

        if (selRect.Width < MinDragDistance)
        {
            if (!addToSelection && vm != null)
            {
                foreach (ClipViewModel clip in vm.Tracks.SelectMany(tr => tr.Clips))
                    clip.IsSelected = false;

                UpdateGlobalSelection(vm);
                _panel.InvalidateVisual();
            }

            return;
        }

        double selStartBeat = (selRect.Left / _panel.PixelsPerBeat) + _panel.BeatOffset;
        double selEndBeat = (selRect.Right / _panel.PixelsPerBeat) + _panel.BeatOffset;

        if (!addToSelection && vm != null)
        {
            foreach (ClipViewModel clip in vm.Tracks.SelectMany(tr => tr.Clips))
                clip.IsSelected = false;
        }

        if (vm != null)
        {
            Rect bounds = _panel.Bounds;
            bool crossesTracks = selRect.Top < 0 || selRect.Bottom > bounds.Height;

            if (crossesTracks)
            {
                SelectAcrossTracks(vm, selRect, selStartBeat, selEndBeat);
            }
            else
            {
                SelectInCurrentTrack(selStartBeat, selEndBeat);
            }

            UpdateGlobalSelection(vm);
        }

        _panel.InvalidateVisual();
    }

    public void Cancel(PointerEventArgs? e = null)
    {
        IsActive = false;

        Release(e);

        _panel.Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void SelectInCurrentTrack(double selStartBeat, double selEndBeat)
    {
        if (_panel.Clips == null)
            return;

        double clipTop = 2.0;
        double clipBottom = Math.Max(1.0, _panel.Bounds.Height - 2.0);

        Rect selRect = new(StartPoint, CurrentPoint);

        foreach (ClipViewModel clip in _panel.Clips)
        {
            double clipStart = clip.StartBeat;
            double clipEnd = clip.StartBeat + clip.DurationBeats;

            if (clipEnd < selStartBeat || clipStart > selEndBeat)
                continue;

            if (selRect.Bottom < clipTop || selRect.Top > clipBottom)
                continue;

            clip.IsSelected = true;
            _lastSelectedClip = clip;
        }
    }

    private void SelectAcrossTracks(TimelineEditorViewModel vm, Rect selRect, double selStartBeat, double selEndBeat)
    {
        List<TrackViewModel> tracks = [.. vm.Tracks];

        int thisIndex = -1;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (ReferenceEquals(tracks[i].Clips, _panel.Clips) ||
                (_panel.Clips != null && tracks[i].Clips.SequenceEqual(_panel.Clips)))
            {
                thisIndex = i;
                break;
            }
        }

        if (thisIndex == -1)
        {
            foreach (ClipViewModel clip in vm.Tracks.SelectMany(tr => tr.Clips))
            {
                double clipStart = clip.StartBeat;
                double clipEnd = clip.StartBeat + clip.DurationBeats;
                if (clipEnd < selStartBeat || clipStart > selEndBeat)
                    continue;
                clip.IsSelected = true;
                _lastSelectedClip = clip;
            }
        }
        else
        {
            double cum = 0.0;
            double panelTop = 0.0;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (i == thisIndex)
                    panelTop = cum;
                cum += tracks[i].Height;
            }

            double selGlobalTop = panelTop + selRect.Top;
            double selGlobalBottom = panelTop + selRect.Bottom;

            double runningTop = 0.0;
            for (int i = 0; i < tracks.Count; i++)
            {
                double trackTop = runningTop;
                double trackBottom = runningTop + tracks[i].Height;
                runningTop = trackBottom;

                if (trackBottom < selGlobalTop || trackTop > selGlobalBottom)
                    continue;

                foreach (ClipViewModel clip in tracks[i].Clips)
                {
                    double clipStart = clip.StartBeat;
                    double clipEnd = clip.StartBeat + clip.DurationBeats;
                    if (clipEnd < selStartBeat || clipStart > selEndBeat)
                        continue;
                    clip.IsSelected = true;
                    _lastSelectedClip = clip;
                }
            }
        }
    }

    private static void UpdateGlobalSelection(TimelineEditorViewModel? vm)
    {
        if (vm == null || Application.Current is not App app)
            return;
        app.TimelineContext.SelectedObjects = [.. vm.Tracks.SelectMany(t => t.Clips)
            .Where(c => c.IsSelected)
            .Cast<object>()];
    }
}