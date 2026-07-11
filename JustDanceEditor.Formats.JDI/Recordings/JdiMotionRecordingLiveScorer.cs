using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI.Recordings;

public enum MotionRecordingClassifierSource
{
    CurrentAndPreviousRecordings,
    ExistingFiles
}

public sealed record MotionRecordingLiveScoringOptions
{
    public MotionRecordingClassifierSource ClassifierSource { get; init; } = MotionRecordingClassifierSource.CurrentAndPreviousRecordings;
    public MotionClassifierGenerationOptions GenerationOptions { get; init; } = new();
    public MoveScoringOptions MoveSpaceOptions { get; init; } = new();
    public MotionRecordingScoringProfile ScoringProfile { get; init; } = MotionRecordingScoringProfile.Raw;
    public float SongScoreMaxScore { get; init; } = 13333.0f;
    public float GoldMoveValue { get; init; } = 3.5f;
}

public sealed record MotionRecordingLiveScoringIssue(string MoveId, string Message);

public sealed record MotionRecordingLiveScore(
    int MoveIndex,
    string MoveId,
    bool IsGoldMove,
    MotionRecordingClassifierSource ClassifierSource,
    int ClassifierExampleCount,
    MotionRecordingMoveFeedback Feedback,
    float PercentageScore,
    float AddedScore,
    float TotalScore,
    MoveSpaceScoreResult? MoveSpace,
    string? Issue)
{
    public string FeedbackText => Feedback.ToString();
}

public sealed class JdiMotionRecordingLiveScorer
{
    public async Task<MotionRecordingLiveScoreSession> CreateSessionAsync(
        string packageRoot,
        IntermediateSongPackage package,
        int coachId,
        IReadOnlyList<MotionRecordingDocument> previousRecordings,
        MotionRecordingLiveScoringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(previousRecordings);
        if (coachId < 0)
            throw new ArgumentOutOfRangeException(nameof(coachId), "Coach IDs cannot be negative.");

        options ??= new MotionRecordingLiveScoringOptions();

        List<MotionRecordingLiveScoringIssue> issues = [];
        MoveTimeline? timeline = JdiMotionRecordingData.FindHandMotionTimeline(package, coachId);
        if (timeline == null)
        {
            issues.Add(new MotionRecordingLiveScoringIssue("", $"No hand-motion timeline exists for coach {coachId}."));
            return new MotionRecordingLiveScoreSession(
                [],
                new Dictionary<string, List<MotionExample>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase),
                string.Empty,
                options,
                issues);
        }

        List<JdiMotionMoveWindow> moveWindows = JdiMotionRecordingData.BuildMoveWindows(package, timeline);
        Dictionary<string, List<MotionExample>> examplesByMove = JdiMotionRecordingData.BuildExamplesByMove(previousRecordings, moveWindows, coachId);
        Dictionary<string, byte[]> existingClassifiers = options.ClassifierSource == MotionRecordingClassifierSource.ExistingFiles
            ? await LoadExistingClassifiersAsync(packageRoot, moveWindows, issues, cancellationToken)
            : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        string songName = string.IsNullOrWhiteSpace(package.Metadata.MapName)
            ? package.Metadata.SongID.ToString("N")
            : package.Metadata.MapName;

        return new MotionRecordingLiveScoreSession(
            moveWindows,
            examplesByMove,
            existingClassifiers,
            songName,
            options,
            issues);
    }

    private static async Task<Dictionary<string, byte[]>> LoadExistingClassifiersAsync(
        string packageRoot,
        IReadOnlyList<JdiMotionMoveWindow> moveWindows,
        List<MotionRecordingLiveScoringIssue> issues,
        CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> result = new(StringComparer.OrdinalIgnoreCase);
        string movesFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.MovesV7Folder);

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

public sealed class MotionRecordingLiveScoreSession
{
    private const double WindowCompletionEpsilon = 0.001;

    private readonly IReadOnlyList<JdiMotionMoveWindow> _moveWindows;
    private readonly Dictionary<string, List<MotionExample>> _examplesByMove;
    private readonly IReadOnlyDictionary<string, byte[]> _existingClassifiers;
    private readonly string _songName;
    private readonly MotionRecordingLiveScoringOptions _options;
    private readonly HashSet<int> _scoredMoveIndexes = [];
    private readonly MotionClassifierGenerator _classifierGenerator = new();
    private readonly MoveSpaceScorer _moveSpaceScorer = new();
    private readonly float _moveScoreValue;
    private readonly float _goldScoreValue;

    internal MotionRecordingLiveScoreSession(
        IReadOnlyList<JdiMotionMoveWindow> moveWindows,
        Dictionary<string, List<MotionExample>> examplesByMove,
        IReadOnlyDictionary<string, byte[]> existingClassifiers,
        string songName,
        MotionRecordingLiveScoringOptions options,
        IReadOnlyList<MotionRecordingLiveScoringIssue> initializationIssues)
    {
        _moveWindows = moveWindows;
        _examplesByMove = examplesByMove;
        _existingClassifiers = existingClassifiers;
        _songName = songName;
        _options = options;
        int goldMoveCount = moveWindows.Count(static move => move.IsGoldMove);
        (_goldScoreValue, _moveScoreValue) = MotionRecordingScoreMath.GetScoreValues(
            moveWindows.Count - goldMoveCount,
            goldMoveCount,
            options.SongScoreMaxScore,
            options.GoldMoveValue);
        InitializationIssues = initializationIssues;
    }

    public IReadOnlyList<MotionRecordingLiveScoringIssue> InitializationIssues { get; }
    public float TotalScore { get; private set; }

    public IReadOnlyList<MotionRecordingLiveScore> ScoreCompletedMoves(
        MotionRecordingDocument currentRecording,
        double currentTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(currentRecording);

        List<JdiMotionMoveWindow> dueMoves = [];
        foreach (JdiMotionMoveWindow move in _moveWindows)
        {
            if (_scoredMoveIndexes.Contains(move.Index))
                continue;

            if (move.StartSeconds < currentRecording.TimelineStartSeconds - WindowCompletionEpsilon)
            {
                _scoredMoveIndexes.Add(move.Index);
                continue;
            }

            if (currentTimeSeconds + WindowCompletionEpsilon < move.EndSeconds)
                continue;

            _scoredMoveIndexes.Add(move.Index);
            dueMoves.Add(move);
        }

        if (dueMoves.Count == 0)
            return [];

        if (_options.ClassifierSource == MotionRecordingClassifierSource.ExistingFiles)
            return ScoreExistingClassifierMoves(currentRecording, dueMoves);

        List<MotionRecordingLiveScore> results = [];
        foreach (JdiMotionMoveWindow move in dueMoves)
        {
            MotionRecordingLiveScore score = ScoreMove(currentRecording, move);
            TotalScore += score.AddedScore;
            results.Add(score with { TotalScore = TotalScore });
        }

        return results;
    }

    private IReadOnlyList<MotionRecordingLiveScore> ScoreExistingClassifierMoves(
        MotionRecordingDocument currentRecording,
        IReadOnlyList<JdiMotionMoveWindow> dueMoves)
    {
        List<MotionRecordingLiveScore> results = [];
        foreach (JdiMotionMoveWindow move in dueMoves)
        {
            MotionRecordingLiveScore score = ScoreExistingClassifierMove(currentRecording, move);
            TotalScore += score.AddedScore;
            results.Add(score with { TotalScore = TotalScore });
        }

        return results;
    }

    private MotionRecordingLiveScore ScoreExistingClassifierMove(
        MotionRecordingDocument currentRecording,
        JdiMotionMoveWindow move)
    {
        List<MotionSample> currentSamples = JdiMotionRecordingData.BuildSmoothedSamples(
            currentRecording.Samples,
            move.StartSeconds,
            move.DurationSeconds);

        MoveSpaceScoreResult? moveSpace = null;
        string? issue = null;

        if (!_existingClassifiers.TryGetValue(move.MoveId, out byte[]? classifierBytes))
        {
            issue = "Existing classifier file was not found.";
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
                    Samples = currentSamples,
                    Options = MotionRecordingScoreMath.ApplyScoringProfileDefaults(
                        _options.MoveSpaceOptions,
                        _options.ScoringProfile) with
                        {
                            FeedMode = MotionSampleFeedMode.LegacyToolInterpolation
                        }
                });

            }
            catch (Exception ex)
            {
                issue = ex.Message;
            }
        }

        MotionRecordingScoreEvaluation evaluation = EvaluateMove(move.IsGoldMove, moveSpace);

        return new MotionRecordingLiveScore(
            move.Index,
            move.MoveId,
            move.IsGoldMove,
            _options.ClassifierSource,
            0,
            evaluation.Feedback,
            evaluation.PercentageScore,
            evaluation.AddedScore,
            TotalScore,
            moveSpace,
            issue);
    }

    private MotionRecordingLiveScore ScoreMove(MotionRecordingDocument currentRecording, JdiMotionMoveWindow move)
    {
        List<MotionSample> currentSamples = JdiMotionRecordingData.BuildSmoothedSamples(
            currentRecording.Samples,
            move.StartSeconds,
            move.DurationSeconds);

        byte[]? classifierBytes = null;
        int exampleCount = 0;
        string? issue = null;

        if (_options.ClassifierSource == MotionRecordingClassifierSource.CurrentAndPreviousRecordings)
        {
            List<MotionExample> examples = GetMoveExamples(move.MoveId);
            if (currentSamples.Count > 0)
                examples.Add(new MotionExample((float)move.DurationSeconds, currentSamples));

            exampleCount = examples.Count;
            if (examples.Count == 0)
            {
                issue = "No recording samples overlapped this move.";
            }
            else
            {
                try
                {
                    classifierBytes = _classifierGenerator.BuildClassifier(new MotionClassifierBuildRequest
                    {
                        SongName = _songName,
                        MoveName = move.MoveId,
                        Examples = examples,
                        Options = _options.GenerationOptions
                    });
                }
                catch (Exception ex)
                {
                    issue = ex.Message;
                }
            }
        }
        else if (!_existingClassifiers.TryGetValue(move.MoveId, out classifierBytes))
        {
            issue = "Existing classifier file was not found.";
        }

        MoveSpaceScoreResult? moveSpace = null;

        if (classifierBytes != null)
        {
            try
            {
                moveSpace = _moveSpaceScorer.ScoreMove(new MoveScoreRequest
                {
                    MoveName = move.MoveId,
                    ClassifierBytes = classifierBytes,
                    Duration = (float)move.DurationSeconds,
                    Samples = currentSamples,
                    Options = MotionRecordingScoreMath.ApplyScoringProfileDefaults(
                        _options.MoveSpaceOptions,
                        _options.ScoringProfile)
                });
            }
            catch (Exception ex)
            {
                issue = ex.Message;
            }
        }

        MotionRecordingScoreEvaluation evaluation = EvaluateMove(move.IsGoldMove, moveSpace);

        return new MotionRecordingLiveScore(
            move.Index,
            move.MoveId,
            move.IsGoldMove,
            _options.ClassifierSource,
            exampleCount,
            evaluation.Feedback,
            evaluation.PercentageScore,
            evaluation.AddedScore,
            TotalScore,
            moveSpace,
            issue);
    }

    private List<MotionExample> GetMoveExamples(string moveId)
    {
        if (_examplesByMove.TryGetValue(moveId, out List<MotionExample>? examples))
            return examples;

        examples = [];
        _examplesByMove.Add(moveId, examples);
        return examples;
    }

    private MotionRecordingScoreEvaluation EvaluateMove(bool isGoldMove, MoveSpaceScoreResult? moveSpace)
        => MotionRecordingScoreMath.EvaluateMove(
            isGoldMove,
            moveSpace,
            _goldScoreValue,
            _moveScoreValue,
            _options.ScoringProfile);
}
