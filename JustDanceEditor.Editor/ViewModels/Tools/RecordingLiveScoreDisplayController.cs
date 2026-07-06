using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingLiveScoreDisplayController(MotionRecordingScoreHudService scoreHud)
{
    private CancellationTokenSource? _feedbackFadeCts;

    public bool IsActive
    {
        get => scoreHud.IsActive;
        set => scoreHud.IsActive = value;
    }

    public void ApplyScores(
        RecordingsToolViewModel target,
        IReadOnlyList<MotionRecordingLiveScore> scores,
        float totalScore)
    {
        target.LiveTotalScore = (int)MathF.Round(totalScore);
        scoreHud.TotalScore = target.LiveTotalScore;
        if (scores.Count == 0)
            return;

        MotionRecordingLiveScore latest = scores[^1];
        if (latest.Issue != null)
            target.StatusText = $"Scoring: {latest.Issue}";

        _ = ShowRecentFeedbackAsync(target, latest);
    }

    public void Clear(RecordingsToolViewModel target, bool clearTotal)
    {
        _feedbackFadeCts?.Cancel();
        _feedbackFadeCts?.Dispose();
        _feedbackFadeCts = null;
        target.RecentMoveFeedbackText = string.Empty;
        target.RecentMoveScoreText = string.Empty;
        target.RecentMoveFeedbackOpacity = 0.0;
        if (clearTotal)
            target.LiveTotalScore = 0;
        scoreHud.Reset(clearTotal);
    }

    private async Task ShowRecentFeedbackAsync(RecordingsToolViewModel target, MotionRecordingLiveScore score)
    {
        _feedbackFadeCts?.Cancel();
        _feedbackFadeCts?.Dispose();
        CancellationTokenSource cts = new();
        _feedbackFadeCts = cts;

        target.RecentMoveFeedbackText = score.FeedbackText;
        target.RecentMoveScoreText = $"+{MathF.Round(score.AddedScore)} {score.MoveId}";
        target.RecentMoveFeedbackOpacity = 1.0;
        scoreHud.FeedbackText = target.RecentMoveFeedbackText;
        scoreHud.MoveScoreText = target.RecentMoveScoreText;
        scoreHud.FeedbackOpacity = target.RecentMoveFeedbackOpacity;

        try
        {
            await Task.Delay(650, cts.Token);
            const int fadeSteps = 10;
            for (int i = 1; i <= fadeSteps; i++)
            {
                await Task.Delay(35, cts.Token);
                target.RecentMoveFeedbackOpacity = 1.0 - (i / (double)fadeSteps);
                scoreHud.FeedbackOpacity = target.RecentMoveFeedbackOpacity;
            }

            target.RecentMoveFeedbackText = string.Empty;
            target.RecentMoveScoreText = string.Empty;
            scoreHud.FeedbackText = string.Empty;
            scoreHud.MoveScoreText = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_feedbackFadeCts, cts))
            {
                _feedbackFadeCts.Dispose();
                _feedbackFadeCts = null;
            }
        }
    }
}