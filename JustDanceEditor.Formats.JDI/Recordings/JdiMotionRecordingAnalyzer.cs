using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Generation;
using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed record MotionRecordingAnalysisOptions
{
    public MoveScoringOptions MoveSpaceOptions { get; init; } = new();
    public MotionRecordingScoringProfile ScoringProfile { get; init; } = MotionRecordingScoringProfile.Raw;
    public float SongScoreMaxScore { get; init; } = 13333.0f;
    public float GoldMoveValue { get; init; } = 3.5f;
}

public sealed record MotionRecordingAnalyzedMove(
    int MoveIndex,
    string MoveId,
    bool IsGoldMove,
    double StartBeatLabel,
    double EndBeatLabel,
    double StartSeconds,
    double EndSeconds,
    MotionRecordingMoveFeedback Feedback,
    float PercentageScore,
    float AddedScore,
    float TotalScore,
    MoveSpaceScoreResult? MoveSpace,
    string? Issue);

public sealed record MotionRecordingMoveAggregate(
    string MoveId,
    int Count,
    float AveragePercentageScore,
    float WorstPercentageScore,
    float BestPercentageScore,
    float TotalAddedScore,
    int WorstMoveIndex);

public sealed record MotionRecordingAnalysisResult(
    int CoachId,
    int MoveCount,
    int ScoredMoveCount,
    int MissingClassifierCount,
    int IssueCount,
    float TotalScore,
    float AveragePercentageScore,
    float AverageScoredPercentageScore,
    IReadOnlyList<MotionRecordingAnalyzedMove> Moves,
    IReadOnlyList<MotionRecordingMoveAggregate> MoveAggregates,
    IReadOnlyList<MotionRecordingLiveScoringIssue> InitializationIssues);

public sealed class JdiMotionRecordingAnalyzer
{
    private readonly MoveSpaceScorer _moveSpaceScorer = new();

    public async Task<MotionRecordingAnalysisResult> AnalyzeExistingClassifiersAsync(
        string packageRoot,
        IntermediateSongPackage package,
        MotionRecordingDocument recording,
        MotionRecordingAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.CoachId < 0)
            throw new ArgumentOutOfRangeException(nameof(recording), "Recording coach IDs cannot be negative.");

        options ??= new MotionRecordingAnalysisOptions();

        List<MotionRecordingLiveScoringIssue> initializationIssues = [];
        MoveTimeline? timeline = JdiMotionRecordingData.FindHandMotionTimeline(package, recording.CoachId);
        if (timeline == null)
        {
            initializationIssues.Add(new MotionRecordingLiveScoringIssue("", $"No hand-motion timeline exists for coach {recording.CoachId}."));
            return new MotionRecordingAnalysisResult(recording.CoachId, 0, 0, 0, 1, 0, 0, 0, [], [], initializationIssues);
        }

        List<JdiMotionMoveWindow> moveWindows = JdiMotionRecordingData.BuildMoveWindows(package, timeline);
        Dictionary<string, byte[]> classifiers = await LoadExistingClassifiersAsync(
            packageRoot,
            moveWindows,
            initializationIssues,
            cancellationToken);

        MotionRecordingLiveScoringOptions scoreOptions = new()
        {
            SongScoreMaxScore = options.SongScoreMaxScore,
            GoldMoveValue = options.GoldMoveValue,
            ScoringProfile = options.ScoringProfile
        };
        (float goldScoreValue, float moveScoreValue) = JdiMotionRecordingScoreMath.GetScoreValues(moveWindows, scoreOptions);
        MoveScoringOptions moveSpaceOptions = JdiMotionRecordingScoreMath.ApplyScoringProfileDefaults(
            options.MoveSpaceOptions,
            options.ScoringProfile) with
            {
                FeedMode = MotionSampleFeedMode.LegacyToolInterpolation
            };

        List<MotionRecordingAnalyzedMove> moves = [];
        float totalScore = 0.0f;
        int missingClassifierCount = 0;
        int issueCount = initializationIssues.Count;

        foreach (JdiMotionMoveWindow move in moveWindows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<MotionSample> samples = JdiMotionRecordingData.BuildSmoothedSamples(
                recording.Samples,
                move.StartSeconds,
                move.DurationSeconds);

            MoveSpaceScoreResult? moveSpace = null;
            string? issue = null;

            if (!classifiers.TryGetValue(move.MoveId, out byte[]? classifierBytes))
            {
                missingClassifierCount++;
                issue = "Existing classifier file was not found.";
            }
            else if (samples.Count == 0)
            {
                issue = "No recording samples overlapped this move.";
            }
            else
            {
                try
                {
                    moveSpace = _moveSpaceScorer.ScoreMove(new MoveScoreRequest
                    {
                        MoveName = move.MoveId,
                        ClassifierBytes = classifierBytes,
                        Duration = (float)move.DurationSeconds,
                        Samples = samples,
                        Options = moveSpaceOptions
                    });
                }
                catch (Exception ex)
                {
                    issue = ex.Message;
                }
            }

            if (!string.IsNullOrWhiteSpace(issue))
                issueCount++;

            MotionRecordingScoreEvaluation evaluation = JdiMotionRecordingScoreMath.EvaluateMove(
                move.IsGoldMove,
                moveSpace,
                goldScoreValue,
                moveScoreValue,
                options.ScoringProfile);
            totalScore += evaluation.AddedScore;

            moves.Add(new MotionRecordingAnalyzedMove(
                move.Index,
                move.MoveId,
                move.IsGoldMove,
                move.StartBeatLabel,
                move.EndBeatLabel,
                move.StartSeconds,
                move.EndSeconds,
                evaluation.Feedback,
                evaluation.PercentageScore,
                evaluation.AddedScore,
                totalScore,
                moveSpace,
                issue));
        }

        List<MotionRecordingMoveAggregate> aggregates = [.. moves
            .GroupBy(static move => move.MoveId, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                MotionRecordingAnalyzedMove worst = group.OrderBy(static move => move.PercentageScore).ThenBy(static move => move.MoveIndex).First();
                return new MotionRecordingMoveAggregate(
                    group.Key,
                    group.Count(),
                    group.Average(static move => move.PercentageScore),
                    group.Min(static move => move.PercentageScore),
                    group.Max(static move => move.PercentageScore),
                    group.Sum(static move => move.AddedScore),
                    worst.MoveIndex);
            })
            .OrderBy(static move => move.AveragePercentageScore)
            .ThenBy(static move => move.MoveId, StringComparer.OrdinalIgnoreCase)];

        int scoredMoveCount = moves.Count(static move => move.MoveSpace != null);
        float averagePercentage = moves.Count == 0 ? 0.0f : moves.Average(static move => move.PercentageScore);
        float averageScoredPercentage = scoredMoveCount == 0
            ? 0.0f
            : moves.Where(static move => move.MoveSpace != null).Average(static move => move.PercentageScore);

        return new MotionRecordingAnalysisResult(
            recording.CoachId,
            moveWindows.Count,
            scoredMoveCount,
            missingClassifierCount,
            issueCount,
            totalScore,
            averagePercentage,
            averageScoredPercentage,
            moves,
            aggregates,
            initializationIssues);
    }

    private static async Task<Dictionary<string, byte[]>> LoadExistingClassifiersAsync(
        string packageRoot,
        IReadOnlyList<JdiMotionMoveWindow> moveWindows,
        List<MotionRecordingLiveScoringIssue> issues,
        CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> result = new(StringComparer.OrdinalIgnoreCase);
        string movesFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.MovesFolder);

        foreach (string moveId in moveWindows.Select(static move => move.MoveId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!JdiMotionRecordingData.IsSafeFileName(moveId))
            {
                issues.Add(new MotionRecordingLiveScoringIssue(moveId, "Move ID cannot be loaded as a classifier file name."));
                continue;
            }

            string path = Path.Combine(movesFolder, moveId + ".msm");
            if (!File.Exists(path))
            {
                issues.Add(new MotionRecordingLiveScoringIssue(moveId, "Existing classifier file was not found."));
                continue;
            }

            result[moveId] = await File.ReadAllBytesAsync(path, cancellationToken);
        }

        return result;
    }
}
