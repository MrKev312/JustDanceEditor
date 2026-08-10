using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Editor.Views.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using System;

namespace JustDanceEditor.Editor.Views.Timeline;

internal sealed class TimelineExternalDropController(
    TimelineTrackPanel panel,
    Func<TimelineEditorViewModel?> getTimeline)
{
    private IBrush? originalBackground;
    private bool highlighting;

    public void OnDragOver(object? sender, DragEventArgs e)
    {
        LibraryItemViewModel? item = LibraryToolView.GetDraggedItem();
        bool valid = item != null && Accepts(item, panel.DataContext as TrackViewModel);
        SetFeedback(item == null ? null : valid);
        e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    public void OnDragEnter(object? sender, DragEventArgs e)
    {
        LibraryItemViewModel? item = LibraryToolView.GetDraggedItem();
        if (item != null)
        {
            SetFeedback(Accepts(item, panel.DataContext as TrackViewModel));
            e.Handled = true;
        }
    }

    public void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearFeedback();
        e.Handled = true;
    }

    public void OnDrop(object? sender, DragEventArgs e)
    {
        ClearFeedback();
        LibraryItemViewModel? item = LibraryToolView.GetDraggedItem();
        TimelineEditorViewModel? timeline = getTimeline();
        if (item == null || panel.DataContext is not TrackViewModel track || timeline == null || !Accepts(item, track))
        {
            Reject(e);
            return;
        }

        Point point = e.GetPosition(panel);
        double beat = SnappingService.FindSnapBeat((point.X / panel.PixelsPerBeat) + panel.BeatOffset, timeline);
        switch (item.Type)
        {
            case ItemType.Pictogram:
                AddPictogram(item, beat, track, timeline);
                break;
            case ItemType.HandMove:
            case ItemType.FullBodyMove:
                AddMove(item, beat, track, timeline);
                break;
            default:
                Reject(e);
                return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private static bool Accepts(LibraryItemViewModel item, TrackViewModel? track)
        => (item.Type, track?.TrackType) switch
        {
            (ItemType.Pictogram, TrackType.Pictogram) => true,
            (ItemType.FullBodyMove, TrackType.CoachFullBody) => true,
            (ItemType.HandMove, TrackType.CoachHand) => true,
            _ => false
        };

    public void ClearFeedback() => SetFeedback(null);

    public void AddPictogramAtBeat(
        string pictogramId,
        double beat,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        int durationFrames = 24)
    {
        beat = ClampBeat(beat, durationFrames / 24.0, timeline);
        PictogramClipViewModel clip = new(new PictogramClip
        {
            PictogramId = pictogramId,
            Duration = durationFrames,
            StartTime = (int)(beat * 24.0)
        }, timeline.RootPath, timeline);
        AddWithUndo(clip, track, timeline);
    }

    public void AddMoveAtBeat(
        string moveId,
        double beat,
        bool isFullBody,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double durationFrames = 24.0,
        bool isGold = false)
    {
        beat = ClampBeat(beat, durationFrames, timeline);
        MoveClipViewModel clip = new(new MoveClip
        {
            MoveId = moveId,
            StartTime = (int)(beat * 24.0),
            IsGoldMove = isGold
        }, timeline.RootPath, timeline, isFullBody, (int)durationFrames);
        AddWithUndo(clip, track, timeline);
    }

    private void SetFeedback(bool? valid)
    {
        if (valid == null)
        {
            if (highlighting)
                panel.Background = originalBackground;
            highlighting = false;
            panel.Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }

        if (!highlighting)
        {
            originalBackground = panel.Background;
            highlighting = true;
        }

        panel.Background = new SolidColorBrush(valid.Value ? Colors.ForestGreen : Colors.DarkRed) { Opacity = 0.2 };
        panel.Cursor = new Cursor(valid.Value ? StandardCursorType.Hand : StandardCursorType.No);
    }

    private void AddPictogram(
        LibraryItemViewModel item,
        double beat,
        TrackViewModel track,
        TimelineEditorViewModel timeline)
    {
        AddPictogramAtBeat(item.Id, beat, track, timeline, (int)item.DefaultDuration);
    }

    private void AddMove(
        LibraryItemViewModel item,
        double beat,
        TrackViewModel track,
        TimelineEditorViewModel timeline)
    {
        AddMoveAtBeat(item.Id, beat, item.Type == ItemType.FullBodyMove, track, timeline, item.DefaultDuration);
    }

    private static double ClampBeat(double beat, double durationBeats, TimelineEditorViewModel timeline)
    {
        double minimum = timeline.TimelineStructure?.StartBeat ?? 0;
        double maximum = (timeline.TimelineStructure?.EndBeat ?? double.MaxValue) - durationBeats;
        return Math.Max(minimum, Math.Min(beat, maximum));
    }

    private static void AddWithUndo(ClipViewModel clip, TrackViewModel track, TimelineEditorViewModel timeline)
    {
        timeline.PushUndo(
            undo: () => track.Clips.Remove(clip),
            redo: () =>
            {
                if (!track.Clips.Contains(clip))
                    track.Clips.Add(clip);
            });
        track.Clips.Add(clip);
    }

    private static void Reject(DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }
}