using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingBrowserController(
    RecordingsToolViewModel owner,
    RecordingLibraryService recordingLibrary,
    JdiMotionRecordingAnalyzer analyzer,
    EditorSettingsService settings)
{
    private int _analysisVersion;

    public async Task RefreshRecordingsAsync(string? selectPath = null)
    {
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        if (timeline == null)
        {
            ClearAll();
            owner.StatusText = "No active timeline";
            return;
        }

        owner.IsBusy = true;
        owner.NotifyViewStateChanged();
        try
        {
            string? previousPath = selectPath ?? owner.SelectedRecording?.Path;
            owner.Recordings.Clear();
            ClearAnalysis();

            IReadOnlyList<RecordingListItem> recordings = await recordingLibrary.LoadRecordingsAsync(timeline);
            foreach (RecordingListItem recording in recordings)
                owner.Recordings.Add(recording);

            owner.SelectedRecording = owner.Recordings.FirstOrDefault(item => string.Equals(item.Path, previousPath, StringComparison.OrdinalIgnoreCase))
                ?? owner.Recordings.FirstOrDefault();

            owner.StatusText = owner.Recordings.Count == 0
                ? "No recordings found"
                : $"Loaded {owner.Recordings.Count} recording(s)";
        }
        catch (Exception ex)
        {
            owner.StatusText = ex.Message;
        }
        finally
        {
            owner.IsBusy = false;
            owner.NotifyViewStateChanged();
        }
    }

    public async Task DeleteRecordingAsync()
    {
        RecordingListItem? selected = owner.SelectedRecording;
        if (selected == null)
            return;

        bool confirmed = await RecordingsToolDialogs.ShowDeleteConfirmationAsync(selected.DisplayName);
        if (!confirmed)
            return;

        try
        {
            File.Delete(selected.Path);
            owner.StatusText = $"Deleted {selected.DisplayName}";
            await RefreshRecordingsAsync();
        }
        catch (Exception ex)
        {
            owner.StatusText = ex.Message;
        }
    }

    public async Task AnalyzeSelectedRecordingAsync()
    {
        int version = Interlocked.Increment(ref _analysisVersion);
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        RecordingListItem? selected = owner.SelectedRecording;

        ClearAnalysis();
        if (timeline == null || selected == null)
        {
            owner.NotifyViewStateChanged();
            return;
        }

        owner.IsBusy = true;
        owner.NotifyViewStateChanged();
        try
        {
            MotionRecordingAnalysisResult result = await analyzer.AnalyzeExistingClassifiersAsync(
                timeline.RootPath,
                timeline.Package,
                selected.Recording,
                CreateAnalysisOptions());

            if (version != _analysisVersion)
                return;

            ApplyAnalysis(timeline, selected, result);
            owner.StatusText = result.IssueCount == 0
                ? "Analysis ready"
                : $"Analysis ready, {result.IssueCount} issue(s)";
        }
        catch (Exception ex)
        {
            if (version == _analysisVersion)
                owner.StatusText = ex.Message;
        }
        finally
        {
            if (version == _analysisVersion)
            {
                owner.IsBusy = false;
                owner.NotifyViewStateChanged();
            }
        }
    }

    private MotionRecordingAnalysisOptions CreateAnalysisOptions()
    {
        MotionRecordingScoringProfile profile = settings.ScoringProfile;
        return new MotionRecordingAnalysisOptions
        {
            ScoringProfile = profile,
            GoldMoveValue = JdiMotionRecordingScoreMath.GetDefaultGoldMoveValue(profile)
        };
    }

    public void FocusMove(int moveIndex)
    {
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        RecordingListItem? recording = owner.SelectedRecording;
        if (timeline == null || recording == null || moveIndex < 0)
            return;

        string coachTrackTitle = $"Coach {recording.CoachId}";
        TrackViewModel? track = timeline.Tracks.FirstOrDefault(track =>
            track.TrackType == TrackType.CoachHand
            && string.Equals(track.Title, coachTrackTitle, StringComparison.OrdinalIgnoreCase));

        if (track == null)
        {
            owner.StatusText = $"No hand timeline for coach {recording.CoachId}";
            return;
        }

        List<MoveClipViewModel> moves = [.. track.Clips
            .OfType<MoveClipViewModel>()
            .Where(static clip => !string.IsNullOrWhiteSpace(clip.MoveId))
            .OrderBy(static clip => clip.RawClip.StartTime)];

        if (moveIndex >= moves.Count)
        {
            owner.StatusText = $"Move #{moveIndex} is no longer on the timeline";
            return;
        }

        timeline.SelectAndCenterClip(moves[moveIndex]);
    }

    public void ClearAll()
    {
        Interlocked.Increment(ref _analysisVersion);
        owner.IsRecordingSetupVisible = false;
        owner.Recordings.Clear();
        owner.SelectedRecording = null;
        ClearAnalysis();
        owner.NotifyViewStateChanged();
    }

    public void ClearAnalysis()
    {
        owner.HasAnalysis = false;
        owner.SummaryItems.Clear();
        owner.WorstIndividualMoves.Clear();
        owner.WorstAverageMoves.Clear();
        owner.MoveDetails.Clear();
        owner.Issues.Clear();
        owner.ScoreGraphPoints = [];
        owner.AccuracyGraphPoints = [];
        owner.GraphTotalScoreText = string.Empty;
        owner.GraphMarkers = [];
    }

    private void ApplyAnalysis(
        TimelineEditorViewModel timeline,
        RecordingListItem selected,
        MotionRecordingAnalysisResult result)
    {
        owner.HasAnalysis = true;
        RecordingAnalysisPresentation presentation = RecordingAnalysisPresenter.Create(timeline, selected, result);

        foreach (RecordingStatItemViewModel item in presentation.SummaryItems)
            owner.SummaryItems.Add(item);

        foreach (RecordingMoveScoreViewModel move in presentation.WorstIndividualMoves)
            owner.WorstIndividualMoves.Add(move);

        foreach (RecordingMoveAggregateViewModel aggregate in presentation.WorstAverageMoves)
            owner.WorstAverageMoves.Add(aggregate);

        foreach (RecordingMoveScoreViewModel move in presentation.MoveDetails)
            owner.MoveDetails.Add(move);

        foreach (RecordingIssueViewModel issue in presentation.Issues)
            owner.Issues.Add(issue);

        owner.GraphMinBeat = presentation.GraphMinBeat;
        owner.GraphMaxBeat = presentation.GraphMaxBeat;
        owner.GraphTotalScoreText = presentation.GraphTotalScoreText;
        owner.GraphMarkers = presentation.GraphMarkers;
        owner.ScoreGraphPoints = presentation.ScoreGraphPoints;
        owner.AccuracyGraphPoints = presentation.AccuracyGraphPoints;

        owner.NotifyViewStateChanged();
    }
}
