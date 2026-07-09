using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed record MotionRecordingMoveScorePreview(
    int RecordingCount,
    int MoveInstanceCount,
    int ScoredMoveCount,
    int XCount,
    int OkCount,
    int GoodCount,
    int SuperCount,
    int PerfectCount,
    int YeahCount,
    float AveragePercentageScore,
    float AverageAddedScore);

public sealed record MotionRecordingMoveScorePoint(
    int RecordingIndex,
    int CoachId,
    int MoveIndex,
    string MoveId,
    bool IsGoldMove,
    double StartBeatLabel,
    double EndBeatLabel,
    MotionRecordingMoveFeedback Feedback,
    float PercentageScore,
    float AdjustedPercentageScore,
    float AddedScore,
    MoveSpaceScoreResult? MoveSpace,
    string? Issue);

public sealed class JdiMotionRecordingMoveScorer
{
    private readonly MoveSpaceScorer _moveSpaceScorer = new();

    public MotionRecordingMoveScorePreview ScoreMove(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> recordings,
        byte[] classifierBytes,
        MoveScoringOptions? options = null,
        MotionRecordingScoringProfile scoringProfile = MotionRecordingScoringProfile.Raw,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(moveId);
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(classifierBytes);

        options ??= new MoveScoringOptions();
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MotionRecordingMoveScorePoint> points = ScoreMoveInstances(
            package,
            moveId,
            recordings,
            classifierBytes,
            options,
            scoringProfile,
            cancellationToken);

        int scoredMoveCount = points.Count(static point => string.IsNullOrWhiteSpace(point.Issue));

        return new MotionRecordingMoveScorePreview(
            recordings.Count,
            points.Count,
            scoredMoveCount,
            points.Count(static point => point.Feedback == MotionRecordingMoveFeedback.X),
            points.Count(static point => point.Feedback == MotionRecordingMoveFeedback.Ok),
            points.Count(static point => point.Feedback == MotionRecordingMoveFeedback.Good),
            points.Count(static point => point.Feedback == MotionRecordingMoveFeedback.Super),
            points.Count(static point => point.Feedback == MotionRecordingMoveFeedback.Perfect),
            points.Count(static point => point.Feedback == MotionRecordingMoveFeedback.Yeah),
            scoredMoveCount == 0 ? 0.0f : points.Where(static point => string.IsNullOrWhiteSpace(point.Issue)).Average(static point => point.PercentageScore),
            scoredMoveCount == 0 ? 0.0f : points.Where(static point => string.IsNullOrWhiteSpace(point.Issue)).Average(static point => point.AddedScore));
    }

    public IReadOnlyList<MotionRecordingMoveScorePoint> ScoreMoveInstances(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> recordings,
        byte[] classifierBytes,
        MoveScoringOptions? options = null,
        MotionRecordingScoringProfile scoringProfile = MotionRecordingScoringProfile.Raw,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(moveId);
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(classifierBytes);

        options ??= new MoveScoringOptions();
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<int, List<JdiMotionMoveWindow>> windowsByCoach = [];
        foreach (MotionRecordingDocument recording in recordings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (recording.CoachId < 0 || windowsByCoach.ContainsKey(recording.CoachId))
                continue;

            MoveTimeline? timeline = JdiMotionRecordingData.FindHandMotionTimeline(package, recording.CoachId);
            if (timeline == null)
                continue;

            windowsByCoach[recording.CoachId] = JdiMotionRecordingData.BuildMoveWindows(package, timeline);
        }

        Dictionary<int, (float GoldScoreValue, float MoveScoreValue)> scoreValuesByCoach = [];
        foreach ((int coachId, List<JdiMotionMoveWindow> windows) in windowsByCoach)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int goldMoveCount = windows.Count(static move => move.IsGoldMove);
            scoreValuesByCoach[coachId] = MotionRecordingScoreMath.GetScoreValues(
                windows.Count - goldMoveCount,
                goldMoveCount,
                songScoreMaxScore: 13333.0f,
                goldMoveValue: MotionRecordingScoreMath.GetDefaultGoldMoveValue(scoringProfile));
        }

        MoveScoringOptions moveSpaceOptions = MotionRecordingScoreMath.ApplyScoringProfileDefaults(
            options,
            scoringProfile) with
            {
                FeedMode = MotionSampleFeedMode.LegacyToolInterpolation
            };

        List<MotionRecordingMoveScorePoint> points = [];
        for (int recordingIndex = 0; recordingIndex < recordings.Count; recordingIndex++)
        {
            MotionRecordingDocument recording = recordings[recordingIndex];
            cancellationToken.ThrowIfCancellationRequested();
            if (!windowsByCoach.TryGetValue(recording.CoachId, out List<JdiMotionMoveWindow>? windows))
                continue;

            (float goldScoreValue, float moveScoreValue) = scoreValuesByCoach[recording.CoachId];
            foreach (JdiMotionMoveWindow window in windows.Where(window => string.Equals(window.MoveId, moveId, StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<MotionSample> samples = JdiMotionRecordingData.BuildSmoothedSamples(
                    recording.Samples,
                    window.StartSeconds,
                    window.DurationSeconds);
                MoveSpaceScoreResult? moveSpace = null;
                float percentage = 0.0f;
                float adjustedPercentage = 0.0f;
                string? issue = null;

                if (samples.Count == 0)
                {
                    issue = "No recording samples overlapped this move.";
                }
                else
                {
                    try
                    {
                        moveSpace = _moveSpaceScorer.ScoreMove(new MoveScoreRequest
                        {
                            MoveName = window.MoveId,
                            ClassifierBytes = classifierBytes,
                            Duration = (float)window.DurationSeconds,
                            Samples = samples,
                            Options = moveSpaceOptions
                        });

                        percentage = MotionRecordingScoreMath.NormalizePercentage(moveSpace.PercentageScore);
                        adjustedPercentage = MotionRecordingScoreMath.GetProfilePercentage(moveSpace, scoringProfile);
                    }
                    catch (Exception ex)
                    {
                        issue = ex.Message;
                    }
                }

                MotionRecordingMoveFeedback feedback = MotionRecordingScoreMath.GetFeedback(
                    window.IsGoldMove,
                    adjustedPercentage,
                    scoringProfile);
                float addedScore = MotionRecordingScoreMath.GetAddedScore(
                    window.IsGoldMove,
                    feedback,
                    adjustedPercentage,
                    goldScoreValue,
                    moveScoreValue,
                    scoringProfile);

                points.Add(new MotionRecordingMoveScorePoint(
                    recordingIndex,
                    recording.CoachId,
                    window.Index,
                    window.MoveId,
                    window.IsGoldMove,
                    window.StartBeatLabel,
                    window.EndBeatLabel,
                    feedback,
                    percentage,
                    adjustedPercentage,
                    addedScore,
                    moveSpace,
                    issue));
            }
        }

        return points;
    }
}
