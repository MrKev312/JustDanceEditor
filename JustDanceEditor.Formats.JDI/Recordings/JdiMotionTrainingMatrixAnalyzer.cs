using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed record MotionTrainingMatrixColumn(
    int MoveIndex,
    long TimelineClipId,
    string MoveId,
    int MoveOccurrence,
    double StartBeat);

public sealed record MotionTrainingMatrixCell(
    int MoveIndex,
    float? PercentageScore,
    float DifferenceFromConsensus,
    bool IsExcluded,
    string? Issue);

public sealed record MotionTrainingMatrixRow(
    Guid RecordingId,
    int CoachId,
    IReadOnlyList<MotionTrainingMatrixCell> Cells);

public sealed record MotionTrainingMatrixResult(
    IReadOnlyList<MotionTrainingMatrixColumn> Columns,
    IReadOnlyList<MotionTrainingMatrixRow> Rows);

public sealed class JdiMotionTrainingMatrixAnalyzer
{
    private readonly MotionClassifierGenerator _classifierGenerator = new();
    private readonly MoveSpaceScorer _moveScorer = new();

    public MotionTrainingMatrixResult Analyze(
        string packageRoot,
        IntermediateSongPackage package,
        int coachId,
        IReadOnlyList<MotionRecordingDocument> recordings,
        MotionTrainingSelectionDocument trainingSelection,
        bool compareToExistingMsms = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(trainingSelection);

        Timelines.MoveTimeline? timeline = JdiMotionRecordingData.FindHandMotionTimeline(package, coachId);
        if (timeline == null)
            return new MotionTrainingMatrixResult([], []);

        List<JdiMotionMoveWindow> windows = JdiMotionRecordingData.BuildMoveWindows(package, timeline);
        MotionTrainingMatrixColumn[] columns =
        [
            .. windows.Select(window => new MotionTrainingMatrixColumn(
                window.Index,
                window.TimelineClipId,
                window.MoveId,
                window.MoveOccurrence,
                window.StartBeatLabel))
        ];
        MotionRecordingDocument[] coachRecordings =
        [
            .. recordings.Where(recording => recording.CoachId == coachId)
        ];
        if (columns.Length == 0 || coachRecordings.Length == 0)
            return new MotionTrainingMatrixResult(columns, []);

        Dictionary<(Guid RecordingId, int MoveIndex), IReadOnlyList<MotionSample>> samples = [];
        foreach (MotionRecordingDocument recording in coachRecordings)
        {
            foreach (JdiMotionMoveWindow window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                samples[(recording.RecordingId, window.Index)] = JdiMotionRecordingData.BuildSmoothedSamples(
                    recording.Samples,
                    window.StartSeconds,
                    window.DurationSeconds);
            }
        }

        string songName = string.IsNullOrWhiteSpace(package.Metadata.MapName)
            ? package.Metadata.SongID.ToString("N")
            : package.Metadata.MapName;
        MoveScoringOptions scoringOptions = new() { FeedMode = MotionSampleFeedMode.LegacyToolInterpolation };
        List<MutableRow> mutableRows = [];
        ClassifierSet? existingClassifiers = compareToExistingMsms
            ? LoadExistingClassifiers(packageRoot, windows)
            : null;

        foreach (MotionRecordingDocument recording in coachRecordings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClassifierSet classifierSet = existingClassifiers ?? BuildPeerClassifiers(
                songName,
                recording.RecordingId,
                coachRecordings,
                windows,
                samples,
                trainingSelection);

            List<MutableCell> cells = [];
            foreach (JdiMotionMoveWindow window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool isExcluded = trainingSelection.IsExcluded(
                    recording.RecordingId,
                    coachId,
                    window.TimelineClipId,
                    window.MoveId,
                    window.MoveOccurrence);
                IReadOnlyList<MotionSample> moveSamples = samples[(recording.RecordingId, window.Index)];
                float? percentage = null;
                string? issue = classifierSet.Issues[window.MoveId];

                if (moveSamples.Count == 0)
                {
                    issue = "No recording samples overlap this move.";
                }
                else if (classifierSet.Classifiers[window.MoveId] is { } classifier)
                {
                    try
                    {
                        MoveSpaceScoreResult result = _moveScorer.ScoreMove(new MoveScoreRequest
                        {
                            MoveName = window.MoveId,
                            ClassifierBytes = classifier,
                            Duration = (float)window.DurationSeconds,
                            Samples = moveSamples,
                            Options = scoringOptions
                        });
                        percentage = MotionRecordingScoreMath.NormalizePercentage(result.PercentageScore);
                    }
                    catch (Exception ex)
                    {
                        issue = ex.Message;
                    }
                }

                cells.Add(new MutableCell(window.Index, percentage, isExcluded, issue));
            }

            mutableRows.Add(new MutableRow(recording.RecordingId, recording.CoachId, cells));
        }

        ApplyColumnDifferences(mutableRows, windows.Count);
        return new MotionTrainingMatrixResult(
            columns,
            [.. mutableRows.Select(row => new MotionTrainingMatrixRow(
                row.RecordingId,
                row.CoachId,
                [.. row.Cells.Select(cell => new MotionTrainingMatrixCell(
                    cell.MoveIndex,
                    cell.PercentageScore,
                    cell.DifferenceFromConsensus,
                    cell.IsExcluded,
                    cell.Issue))]))]);
    }

    private ClassifierSet BuildPeerClassifiers(
        string songName,
        Guid excludedRecordingId,
        IReadOnlyList<MotionRecordingDocument> recordings,
        IReadOnlyList<JdiMotionMoveWindow> windows,
        IReadOnlyDictionary<(Guid RecordingId, int MoveIndex), IReadOnlyList<MotionSample>> samples,
        MotionTrainingSelectionDocument selection)
    {
        ClassifierSet result = new();
        foreach (string moveId in GetMoveIds(windows))
        {
            IReadOnlyList<MotionExample> examples = BuildTrainingExamples(
                recordings,
                windows,
                samples,
                selection,
                moveId,
                excludedRecordingId);
            if (examples.Count == 0)
            {
                examples = BuildTrainingExamples(
                    recordings,
                    windows,
                    samples,
                    selection,
                    moveId,
                    excludedRecordingId: null);
            }

            if (examples.Count == 0)
            {
                result.SetIssue(moveId, "No included samples are available for this move.");
                continue;
            }

            try
            {
                result.SetClassifier(moveId, _classifierGenerator.BuildClassifier(new MotionClassifierBuildRequest
                {
                    SongName = songName,
                    MoveName = moveId,
                    Examples = examples,
                    Options = new MotionClassifierGenerationOptions
                    {
                        ClassifierFormatVersion = MotionClassifierFormatVersion.Version7
                    }
                }));
            }
            catch (Exception ex)
            {
                result.SetIssue(moveId, ex.Message);
            }
        }

        return result;
    }

    private static ClassifierSet LoadExistingClassifiers(
        string packageRoot,
        IReadOnlyList<JdiMotionMoveWindow> windows)
    {
        ClassifierSet result = new();
        foreach (string moveId in GetMoveIds(windows))
        {
            if (!JdiMotionRecordingData.IsSafeFileName(moveId))
            {
                result.SetIssue(moveId, "The move ID cannot be used as an MSM file name.");
                continue;
            }

            string path = JdiMotionClassifierStorage.GetVersion7Path(packageRoot, moveId + ".msm");
            if (!File.Exists(path))
            {
                result.SetIssue(moveId, "No existing version 7 MSM is available for this move.");
                continue;
            }

            try
            {
                result.SetClassifier(moveId, File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.SetIssue(moveId, ex.Message);
            }
        }

        return result;
    }

    private static IEnumerable<string> GetMoveIds(IReadOnlyList<JdiMotionMoveWindow> windows)
        => windows.Select(static window => window.MoveId).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<MotionExample> BuildTrainingExamples(
        IReadOnlyList<MotionRecordingDocument> recordings,
        IReadOnlyList<JdiMotionMoveWindow> windows,
        IReadOnlyDictionary<(Guid RecordingId, int MoveIndex), IReadOnlyList<MotionSample>> samples,
        MotionTrainingSelectionDocument selection,
        string moveId,
        Guid? excludedRecordingId)
    {
        List<MotionExample> result = [];
        foreach (MotionRecordingDocument recording in recordings)
        {
            if (excludedRecordingId == recording.RecordingId)
                continue;

            foreach (JdiMotionMoveWindow window in windows.Where(window => string.Equals(window.MoveId, moveId, StringComparison.OrdinalIgnoreCase)))
            {
                if (selection.IsExcluded(
                    recording.RecordingId,
                    recording.CoachId,
                    window.TimelineClipId,
                    window.MoveId,
                    window.MoveOccurrence))
                {
                    continue;
                }

                IReadOnlyList<MotionSample> moveSamples = samples[(recording.RecordingId, window.Index)];
                if (moveSamples.Count > 0)
                    result.Add(new MotionExample((float)window.DurationSeconds, moveSamples));
            }
        }

        return result;
    }

    private static void ApplyColumnDifferences(IReadOnlyList<MutableRow> rows, int columnCount)
    {
        for (int column = 0; column < columnCount; column++)
        {
            float[] includedScores =
            [
                .. rows.Select(row => row.Cells[column])
                    .Where(static cell => !cell.IsExcluded && cell.PercentageScore.HasValue)
                    .Select(static cell => cell.PercentageScore!.Value)
            ];
            float consensus = GetConsensusScore(includedScores);
            foreach (MutableRow row in rows)
            {
                MutableCell cell = row.Cells[column];
                cell.DifferenceFromConsensus = cell.PercentageScore.HasValue
                    ? Math.Abs(consensus - cell.PercentageScore.Value)
                    : 100;
            }
        }
    }

    internal static float GetConsensusScore(IReadOnlyList<float> scores)
    {
        if (scores.Count == 0)
            return 0;

        float[] sorted = [.. scores.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2.0f
            : sorted[middle];
    }

    private sealed record MutableRow(Guid RecordingId, int CoachId, List<MutableCell> Cells);

    private sealed class ClassifierSet
    {
        public Dictionary<string, byte[]?> Classifiers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> Issues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void SetClassifier(string moveId, byte[] classifier)
        {
            Classifiers[moveId] = classifier;
            Issues[moveId] = null;
        }

        public void SetIssue(string moveId, string issue)
        {
            Classifiers[moveId] = null;
            Issues[moveId] = issue;
        }
    }

    private sealed class MutableCell(int moveIndex, float? percentageScore, bool isExcluded, string? issue)
    {
        public int MoveIndex { get; } = moveIndex;
        public float? PercentageScore { get; } = percentageScore;
        public bool IsExcluded { get; } = isExcluded;
        public string? Issue { get; } = issue;
        public float DifferenceFromConsensus { get; set; }
    }
}
