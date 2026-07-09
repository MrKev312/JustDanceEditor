using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Views.Timeline.Interactions;

internal sealed class TimelineTrackContextMenuController(TimelineTrackPanel owner)
{
    public void Open(PointerPressedEventArgs? pointer, ClipViewModel? clickedClip)
    {
        TimelineTrackPanel.CurrentContextMenu?.Close();

        ContextMenu menu = new();
        TimelineEditorViewModel? timeline = owner.GetTimelineVM();
        if (owner.DataContext is not TrackViewModel track || timeline == null)
        {
            Add(menu, "Add Clip", async () => await ShowCreateClipAsync(pointer));
            Open(menu);
            return;
        }

        if (track.TrackType == TrackType.Pictogram && clickedClip is PictogramClipViewModel pictogram)
        {
            Add(menu, "Select All Instances", () => SelectAllPictogramInstances(timeline, pictogram));
            AddSeparator(menu);
            Add(menu, "Regenerate Pictogram", async () => await RegeneratePictogramAsync(timeline, pictogram));
            Add(menu, "Flip", async () => await FlipSelectedPictogramsAsync(timeline));
        }
        else if (clickedClip is MoveClipViewModel move)
        {
            Add(menu, "Select All Instances", () => SelectAllMoveInstances(timeline, move));
        }
        else
        {
            Add(menu, "Add Clip", async () => await ShowCreateClipAsync(pointer));
            if (track.TrackType == TrackType.Pictogram)
            {
                AddSeparator(menu);
                Add(menu, "Generate New Pictogram", async () => await GenerateNewPictogramAsync(timeline));
            }
        }

        Open(menu);
    }

    private void Open(ContextMenu menu)
    {
        TimelineTrackPanel.CurrentContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (TimelineTrackPanel.CurrentContextMenu == menu)
                TimelineTrackPanel.CurrentContextMenu = null;
        };
        menu.Open(owner);
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => action();
        (menu.Items as System.Collections.IList)?.Add(item);
    }

    private static void AddSeparator(ContextMenu menu)
        => (menu.Items as System.Collections.IList)?.Add(new Separator());

    private void SelectAllPictogramInstances(TimelineEditorViewModel timeline, PictogramClipViewModel clip)
    {
        _ = timeline.SelectAllPictogramInstances(clip.PictogramId);
        PublishSelection(timeline);
    }

    private void SelectAllMoveInstances(TimelineEditorViewModel timeline, MoveClipViewModel clip)
    {
        _ = timeline.SelectAllMoveInstances(clip.MoveId);
        PublishSelection(timeline);
    }

    private void PublishSelection(TimelineEditorViewModel timeline)
    {
        if (timeline.Services.TimelineContext != null)
        {
            timeline.Services.TimelineContext.SelectedObjects = [.. timeline.Tracks
                .SelectMany(static track => track.Clips)
                .Where(static clip => clip.IsSelected)
                .Cast<object>()];
        }
        owner.InvalidateVisual();
    }

    private static async Task FlipSelectedPictogramsAsync(TimelineEditorViewModel timeline)
    {
        List<PictogramClipViewModel> selected = [.. timeline.Tracks
            .SelectMany(static track => track.Clips)
            .OfType<PictogramClipViewModel>()
            .Where(static clip => clip.IsSelected)];

        foreach (PictogramClipViewModel clip in selected)
            await timeline.FlipPictogramAsync(clip.PictogramId, clip);
    }

    private static async Task GenerateNewPictogramAsync(TimelineEditorViewModel timeline)
    {
        if (timeline.Services.Dialogs == null)
            return;

        PictogramScreenshotOptionsViewModel options = new();
        options.EnableInsertionOptions(BuildReferenceMoveOptions(timeline));
        PictogramScreenshotOptionsResult? result =
            await timeline.Services.Dialogs.ShowDialogAsync<PictogramScreenshotOptionsResult>(options);
        if (result != null)
            await timeline.GenerateNewPictogramAsync(timeline.CurrentBeat, result);
    }

    private static async Task RegeneratePictogramAsync(
        TimelineEditorViewModel timeline,
        PictogramClipViewModel clip)
    {
        if (timeline.Services.Dialogs == null)
            return;

        PictogramScreenshotOptionsResult? result =
            await timeline.Services.Dialogs.ShowDialogAsync<PictogramScreenshotOptionsResult>(new PictogramScreenshotOptionsViewModel());
        if (result != null)
            await timeline.RegeneratePictogramAsync(clip, result);
    }

    private static IReadOnlyList<PictogramReferenceMoveOption> BuildReferenceMoveOptions(TimelineEditorViewModel timeline)
    {
        const double selectionWindowSeconds = 5.0;
        double playheadSeconds = timeline.GetPlaybackSecondsAtBeatLabel(timeline.CurrentBeat);
        return [.. timeline.Tracks
            .Where(static track => track.TrackType is TrackType.CoachHand or TrackType.CoachFullBody)
            .SelectMany(static track => track.Clips)
            .OfType<MoveClipViewModel>()
            .Where(static clip => !string.IsNullOrWhiteSpace(clip.MoveId))
            .GroupBy(static clip => (clip.MoveId, clip.RawClip.StartTime))
            .Select(static group => new PictogramReferenceMoveOption
            {
                MoveId = group.Key.MoveId,
                StartFrame = group.Key.StartTime
            })
            .Select(option => new
            {
                Option = option,
                DistanceSeconds = Math.Abs(timeline.GetPlaybackSecondsAtBeatLabel(option.StartBeat) - playheadSeconds)
            })
            .Where(item => item.DistanceSeconds <= selectionWindowSeconds)
            .OrderBy(static item => item.DistanceSeconds)
            .ThenBy(static item => item.Option.StartFrame)
            .Select(static item => item.Option)];
    }

    private async Task ShowCreateClipAsync(PointerPressedEventArgs? pointer)
    {
        TimelineEditorViewModel? timeline = owner.GetTimelineVM();
        if (owner.DataContext is not TrackViewModel track || timeline?.Services.Dialogs == null)
            return;

        Point position = pointer?.GetPosition(owner) ?? default;
        double beat = (position.X / owner.PixelsPerBeat) + owner.BeatOffset;
        IDialogService dialogs = timeline.Services.Dialogs;

        switch (track.TrackType)
        {
            case TrackType.Pictogram:
                await CreatePictogramAsync(dialogs, track, timeline, beat);
                break;
            case TrackType.CoachHand:
                await CreateMoveAsync(dialogs, track, timeline, beat, isFullBody: false);
                break;
            case TrackType.CoachFullBody:
                await CreateMoveAsync(dialogs, track, timeline, beat, isFullBody: true);
                break;
            case TrackType.HideHud:
                await CreateHideHudAsync(dialogs, track, timeline, beat);
                break;
            case TrackType.Lyrics:
                await CreateLyricsAsync(dialogs, track, timeline, beat);
                break;
            case TrackType.GoldEffect:
                await CreateGoldEffectAsync(dialogs, track, timeline, beat);
                break;
            default:
                await CreateFallbackLyricsAsync(track, timeline, beat);
                break;
        }
    }

    private async Task CreatePictogramAsync(
        IDialogService dialogs,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double beat)
    {
        PictogramCreationResult? result = await dialogs.ShowDialogAsync<PictogramCreationResult>(
            new PictogramCreationViewModel(timeline.AvailablePictograms, timeline.RootPath));
        if (result != null)
        {
            owner.ExternalDropController.AddPictogramAtBeat(
                result.PictogramId,
                SnappingService.FindSnapBeat(beat, timeline),
                track,
                timeline,
                result.Frames);
        }
    }

    private async Task CreateMoveAsync(
        IDialogService dialogs,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double beat,
        bool isFullBody)
    {
        IEnumerable<string> moves = isFullBody
            ? timeline.AvailableFullBodyCoachMoves
            : timeline.AvailableHandCoachMoves;
        MoveCreationViewModel viewModel = new(
            moves,
            isFullBody,
            moveId => (int)timeline.GetOrRegisterMove(moveId, isFullBody).DefaultDuration);
        MoveCreationResult? result = await dialogs.ShowDialogAsync<MoveCreationResult>(viewModel);
        if (result != null)
        {
            owner.ExternalDropController.AddMoveAtBeat(
                result.MoveId,
                SnappingService.FindSnapBeat(beat, timeline),
                isFullBody,
                track,
                timeline,
                result.Frames,
                result.IsGold);
        }
    }

    private static async Task CreateHideHudAsync(
        IDialogService dialogs,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double beat)
    {
        HideHudCreationResult? result = await dialogs.ShowDialogAsync<HideHudCreationResult>(new HideHudCreationViewModel());
        if (result == null)
            return;

        HideUserInterfaceClip raw = new()
        {
            StartTime = (int)(ClampBeat(timeline, beat, result.Frames) * 24),
            Duration = result.Frames,
            IsActive = true
        };
        AddClip(track, timeline, new HideUserInterfaceClipViewModel(raw, timeline.RootPath, timeline));
    }

    private static async Task CreateLyricsAsync(
        IDialogService dialogs,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double beat)
    {
        LyricsCreationResult? result = await dialogs.ShowDialogAsync<LyricsCreationResult>(new LyricsCreationViewModel());
        if (result == null)
            return;

        KaraokeClip raw = new()
        {
            Lyrics = result.Lyrics,
            Duration = result.Frames,
            IsEndOfLine = result.IsEndOfLine,
            StartTime = (int)(ClampBeat(timeline, beat, result.Frames) * 24)
        };
        AddClip(track, timeline, new KaraokeClipViewModel(raw, timeline.RootPath, timeline));
    }

    private static async Task CreateGoldEffectAsync(
        IDialogService dialogs,
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double beat)
    {
        GoldEffectCreationResult? result = await dialogs.ShowDialogAsync<GoldEffectCreationResult>(new GoldEffectCreationViewModel());
        if (result == null)
            return;

        GoldEffectClip raw = new()
        {
            Duration = result.Frames,
            EffectType = result.EffectType,
            StartTime = (int)(ClampBeat(timeline, beat, result.Frames) * 24)
        };
        AddClip(track, timeline, new GoldEffectClipViewModel(raw, timeline.RootPath, timeline));
    }

    private async Task CreateFallbackLyricsAsync(
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        double beat)
    {
        string? lyrics = await TimelineTextInputDialog.ShowAsync(
            owner,
            "Enter lyrics text (empty to create placeholder):");
        if (lyrics == null)
            return;

        KaraokeClip raw = new()
        {
            Lyrics = lyrics,
            Duration = 24,
            StartTime = (int)(ClampBeat(timeline, beat, 24) * 24)
        };
        AddClip(track, timeline, new KaraokeClipViewModel(raw, timeline.RootPath, timeline));
    }

    private static double ClampBeat(TimelineEditorViewModel timeline, double beat, int frames)
    {
        double snapped = SnappingService.FindSnapBeat(beat, timeline);
        double maxStart = timeline.TimelineStructure.EndBeat - (frames / 24d);
        return Math.Max(timeline.TimelineStructure.StartBeat, Math.Min(snapped, maxStart));
    }

    private static void AddClip(
        TrackViewModel track,
        TimelineEditorViewModel timeline,
        ClipViewModel clip)
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
}
