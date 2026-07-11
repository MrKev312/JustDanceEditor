using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Scoring;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class ScoringAdjustmentPreviewAnalyzer : IScoringAdjustmentPreviewAnalyzer
{
    internal const float CurveMaxDistance = 6.0f;
    private const int CandidateCount = 26;
    private readonly JsonMotionRecordingRepository recordingRepository = new();
    private readonly JdiMotionRecordingMoveScorer scorer = new();

    public async Task<ScoringAdjustmentPreviewResult> AnalyzeAsync(
        IntermediateSongPackage package,
        string rootPath,
        string moveId,
        byte[] classifierBytes,
        ScoringAdjustmentDraft draft,
        MotionRecordingScoringProfile scoringProfile,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MotionRecordingDocument> documents = await LoadRecordingsAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => Analyze(package, moveId, classifierBytes, draft, scoringProfile, documents, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public static ScoringAdjustmentCurve CreateCurve(
        ScoringAdjustmentDraft draft,
        IReadOnlyList<ScoringAdjustmentSample>? samples = null,
        int recordingCount = 0,
        int moveInstanceCount = 0)
    {
        double low = ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.LowThreshold);
        double high = ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.HighThreshold);
        List<ScoringAdjustmentCurvePoint> points =
        [.. new[] { 0.0f, (float)low, (float)high, CurveMaxDistance }
            .Where(static distance => float.IsFinite(distance) && distance >= 0.0f && distance <= CurveMaxDistance)
            .Distinct()
            .Order()
            .Select(distance => new ScoringAdjustmentCurvePoint(
                distance,
                ProjectDistancePercentage(distance, low, high)))];

        List<ScoringAdjustmentCurveSample> curveSamples = [];
        int seriesIndex = 0;
        foreach (ScoringAdjustmentSample sample in (samples ?? []).OrderBy(static item => item.RecordingIndex)
            .ThenBy(static item => item.CoachId)
            .ThenBy(static item => item.MoveIndex))
        {
            curveSamples.Add(new(
                sample.Label,
                seriesIndex++,
                sample.RecordingIndex,
                sample.CoachId,
                sample.MoveIndex,
                sample.MoveSpace.StatisticalDistance,
                ProjectDistancePercentage(sample.MoveSpace.StatisticalDistance, low, high)));
        }

        return new(points, curveSamples, points.Count, recordingCount, moveInstanceCount, curveSamples.Count);
    }

    public static IReadOnlyList<ScoringAdjustmentSweepSeries> ProjectThresholdSweep(
        IReadOnlyList<ScoringAdjustmentSample> samples,
        ScoringAdjustmentDraft draft,
        ScoringAdjustmentParameter parameter,
        MotionRecordingScoringProfile scoringProfile)
    {
        Dictionary<ScoringMoveKey, SeriesBuilder> builders = CreateBuilders(samples);
        bool lowSweep = parameter == ScoringAdjustmentParameter.LowThreshold;
        double fixedLow = ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.LowThreshold);
        double fixedHigh = ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.HighThreshold);

        foreach (ScoringAdjustmentCandidate candidate in ScoringAdjustmentDraftMapper.Candidates(parameter))
        {
            double low = lowSweep ? candidate.Value : fixedLow;
            double high = lowSweep ? fixedHigh : candidate.Value;
            foreach (ScoringAdjustmentSample sample in samples)
            {
                MoveSpaceScoreResult projected = ProjectMoveSpace(sample.MoveSpace, low, high, draft);
                builders[sample.Key].Points.Add(new(
                    candidate.Value,
                    MotionRecordingScoreMath.GetProfilePercentage(projected, scoringProfile),
                    candidate.IsDefault));
            }
        }

        return BuildSeries(builders);
    }

    private ScoringAdjustmentPreviewResult Analyze(
        IntermediateSongPackage package,
        string moveId,
        byte[] classifierBytes,
        ScoringAdjustmentDraft draft,
        MotionRecordingScoringProfile scoringProfile,
        IReadOnlyList<MotionRecordingDocument> documents,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MotionRecordingMoveScorePoint> savedPoints = Score(
            package, moveId, documents, classifierBytes, scoringProfile, cancellationToken);
        byte[] primitiveBytes = ScoringAdjustmentDraftMapper.Write(
            classifierBytes,
            ScoringAdjustmentDraftMapper.ForPrimitiveProjection(draft));
        IReadOnlyList<MotionRecordingMoveScorePoint> primitivePoints = Score(
            package, moveId, documents, primitiveBytes, scoringProfile, cancellationToken);
        IReadOnlyList<ScoringAdjustmentSample> samples = CreateSamples(savedPoints, primitivePoints);

        Dictionary<ScoringAdjustmentParameter, IReadOnlyList<ScoringAdjustmentSweepSeries>> sweeps =
            new()
            {
                [ScoringAdjustmentParameter.LowThreshold] = ProjectThresholdSweep(samples, draft, ScoringAdjustmentParameter.LowThreshold, scoringProfile),
                [ScoringAdjustmentParameter.HighThreshold] = ProjectThresholdSweep(samples, draft, ScoringAdjustmentParameter.HighThreshold, scoringProfile)
            };

        (IReadOnlyList<ScoringAdjustmentSweepSeries> autoSweep, IReadOnlyDictionary<double, int> occurrenceCounts) =
            BuildScoredSweep(package, moveId, documents, classifierBytes, savedPoints, draft,
                ScoringAdjustmentParameter.AutoCorrelationThreshold, scoringProfile, cancellationToken);
        sweeps[ScoringAdjustmentParameter.AutoCorrelationThreshold] = autoSweep;
        sweeps[ScoringAdjustmentParameter.DirectionImpactFactor] = BuildScoredSweep(
            package, moveId, documents, classifierBytes, savedPoints, draft,
            ScoringAdjustmentParameter.DirectionImpactFactor, scoringProfile, cancellationToken).Series;

        ScoringAdjustmentRecommendation? recommendation = BuildRecommendation(samples, autoSweep, occurrenceCounts);
        return new(
            draft,
            sweeps,
            samples,
            recommendation,
            CandidateCount,
            documents.Count,
            savedPoints.Count,
            samples.Count);
    }

    private async Task<IReadOnlyList<MotionRecordingDocument>> LoadRecordingsAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        List<MotionRecordingDocument> documents = [];
        foreach (string path in recordingRepository.ListRecordingFiles(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                documents.Add(await recordingRepository.LoadAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                EditorLog.Fallback(ex, $"Load scoring preview recording '{path}'");
            }
        }

        return documents;
    }

    private (IReadOnlyList<ScoringAdjustmentSweepSeries> Series, IReadOnlyDictionary<double, int> OccurrenceCounts) BuildScoredSweep(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> documents,
        byte[] classifierBytes,
        IReadOnlyList<MotionRecordingMoveScorePoint> savedPoints,
        ScoringAdjustmentDraft draft,
        ScoringAdjustmentParameter parameter,
        MotionRecordingScoringProfile scoringProfile,
        CancellationToken cancellationToken)
    {
        Dictionary<ScoringMoveKey, SeriesBuilder> builders = CreateBuilders(savedPoints);
        Dictionary<double, int> occurrenceCounts = [];
        foreach (ScoringAdjustmentCandidate candidate in ScoringAdjustmentDraftMapper.Candidates(parameter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScoringAdjustmentDraft candidateDraft = ScoringAdjustmentDraftMapper.WithCandidate(draft, parameter, candidate);
            byte[] bytes = ScoringAdjustmentDraftMapper.Write(classifierBytes, candidateDraft);
            IReadOnlyList<MotionRecordingMoveScorePoint> points = Score(
                package, moveId, documents, bytes, scoringProfile, cancellationToken);

            IReadOnlyList<MotionRecordingMoveScorePoint> countPoints = points;
            if (parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold && draft.IgnoreAutocorrelation)
            {
                bytes = ScoringAdjustmentDraftMapper.Write(classifierBytes, candidateDraft with { IgnoreAutocorrelation = false });
                countPoints = Score(package, moveId, documents, bytes, scoringProfile, cancellationToken);
            }

            if (parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold)
            {
                occurrenceCounts[Math.Round(candidate.Value, 6)] = countPoints.Count(static point =>
                    string.IsNullOrWhiteSpace(point.Issue) && point.MoveSpace?.AutoCorrelationTime > -6.0f);
            }

            foreach (MotionRecordingMoveScorePoint point in points.Where(static item => string.IsNullOrWhiteSpace(item.Issue)))
            {
                ScoringMoveKey key = ScoringMoveKey.From(point);
                if (!builders.TryGetValue(key, out SeriesBuilder? builder))
                {
                    builder = new SeriesBuilder(point, PreviewAccuracy(point));
                    builders.Add(key, builder);
                }

                builder.Points.Add(new(candidate.Value, PreviewAccuracy(point), candidate.IsDefault));
            }
        }

        return (BuildSeries(builders), occurrenceCounts);
    }

    private IReadOnlyList<MotionRecordingMoveScorePoint> Score(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> documents,
        byte[] classifierBytes,
        MotionRecordingScoringProfile scoringProfile,
        CancellationToken cancellationToken)
        => scorer.ScoreMoveInstances(
            package,
            moveId,
            documents,
            classifierBytes,
            scoringProfile: scoringProfile,
            cancellationToken: cancellationToken);

    private static IReadOnlyList<ScoringAdjustmentSample> CreateSamples(
        IReadOnlyList<MotionRecordingMoveScorePoint> savedPoints,
        IReadOnlyList<MotionRecordingMoveScorePoint> primitivePoints)
    {
        Dictionary<ScoringMoveKey, MotionRecordingMoveScorePoint> savedByKey = savedPoints.ToDictionary(ScoringMoveKey.From);
        return [.. primitivePoints
            .Where(static point => string.IsNullOrWhiteSpace(point.Issue) && point.MoveSpace != null)
            .Select(point =>
            {
                ScoringMoveKey key = ScoringMoveKey.From(point);
                float saved = savedByKey.TryGetValue(key, out MotionRecordingMoveScorePoint? savedPoint)
                    ? PreviewAccuracy(savedPoint)
                    : PreviewAccuracy(point);
                return new ScoringAdjustmentSample(
                    key,
                    CreateLabel(point),
                    point.RecordingIndex,
                    point.CoachId,
                    point.MoveIndex,
                    saved,
                    point.MoveSpace!);
            })];
    }

    private static Dictionary<ScoringMoveKey, SeriesBuilder> CreateBuilders(IEnumerable<ScoringAdjustmentSample> samples)
        => samples.ToDictionary(static item => item.Key, static item => new SeriesBuilder(item));

    private static Dictionary<ScoringMoveKey, SeriesBuilder> CreateBuilders(IEnumerable<MotionRecordingMoveScorePoint> points)
        => points.Where(static point => string.IsNullOrWhiteSpace(point.Issue))
            .GroupBy(ScoringMoveKey.From)
            .ToDictionary(static group => group.Key, static group => new SeriesBuilder(group.First(), PreviewAccuracy(group.First())));

    private static IReadOnlyList<ScoringAdjustmentSweepSeries> BuildSeries(Dictionary<ScoringMoveKey, SeriesBuilder> builders)
    {
        List<ScoringAdjustmentSweepSeries> result = [];
        foreach (SeriesBuilder builder in builders.Values.OrderBy(static item => item.RecordingIndex)
            .ThenBy(static item => item.CoachId)
            .ThenBy(static item => item.MoveIndex))
        {
            IReadOnlyList<ScoringAdjustmentSweepPoint> points =
            [
                .. builder.Points.Where(static point => point.IsDefaultValue).Take(1),
                .. builder.Points.Where(static point => !point.IsDefaultValue).OrderBy(static point => point.Value)
            ];
            if (points.Count > 0)
            {
                result.Add(new(
                    builder.Label,
                    result.Count,
                    builder.RecordingIndex,
                    builder.CoachId,
                    builder.MoveIndex,
                    builder.SavedAccuracy,
                    points));
            }
        }

        return result;
    }

    private static ScoringAdjustmentRecommendation? BuildRecommendation(
        IReadOnlyList<ScoringAdjustmentSample> samples,
        IReadOnlyList<ScoringAdjustmentSweepSeries> autoSweep,
        IReadOnlyDictionary<double, int> occurrenceCounts)
    {
        if (samples.Count == 0)
            return null;

        List<double> distances = [.. samples.Select(static sample => (double)sample.MoveSpace.StatisticalDistance)
            .Where(double.IsFinite).Where(static value => value >= 0.0).Order()];
        if (distances.Count == 0)
            return null;

        (double lowMin, double lowMax) = ScoringAdjustmentDraftMapper.Range(ScoringAdjustmentParameter.LowThreshold);
        (double highMin, double highMax) = ScoringAdjustmentDraftMapper.Range(ScoringAdjustmentParameter.HighThreshold);
        double low = Math.Clamp(Quantile(distances, 0.75), lowMin, lowMax);
        double rough = Quantile(distances, 0.95);
        double high = Math.Clamp(Math.Max(rough + Math.Max(0.25, (rough - low) * 0.5), low + 0.75), highMin, highMax);

        List<double> candidates = [.. autoSweep.SelectMany(static series => series.RangePoints)
            .Select(static point => Math.Round(point.Value, 6)).Distinct().Order()];
        double autoThreshold = candidates.FirstOrDefault(ScoringAdjustmentDraftMapper.Range(ScoringAdjustmentParameter.AutoCorrelationThreshold).Max);
        int autoCount = occurrenceCounts.GetValueOrDefault(Math.Round(autoThreshold, 6));
        for (int index = candidates.Count - 1; index >= 0; index--)
        {
            double candidate = candidates[index];
            int count = occurrenceCounts.GetValueOrDefault(Math.Round(candidate, 6));
            if (count / (double)samples.Count <= 0.01)
            {
                autoThreshold = candidate;
                autoCount = count;
                continue;
            }

            int saferIndex = Math.Min(candidates.Count - 1, index + 1);
            autoThreshold = candidates[saferIndex];
            autoCount = occurrenceCounts.GetValueOrDefault(Math.Round(autoThreshold, 6));
            break;
        }

        double negativeDirectionSum = samples.Where(static sample => !sample.MoveSpace.DirectionTendencyIgnored)
            .Sum(static sample => Math.Max(0.0, -Finite(sample.MoveSpace.DirectionTendencyImpactOnScoreRatio)));
        double directionSensitivity = negativeDirectionSum <= 0.000001
            ? 1.0
            : ScoringAdjustmentDraftMapper.ClampUnit(0.1 * Math.Floor(0.01 * 10.0 * samples.Count / negativeDirectionSum));

        return new(
            low,
            high,
            distances[0],
            Quantile(distances, 0.5),
            distances[^1],
            autoThreshold,
            ScoringAdjustmentDraftMapper.SensitivityFromAutoCorrelation(autoThreshold),
            autoCount,
            directionSensitivity,
            negativeDirectionSum,
            samples.Count);
    }

    private static MoveSpaceScoreResult ProjectMoveSpace(
        MoveSpaceScoreResult source,
        double lowThreshold,
        double highThreshold,
        ScoringAdjustmentDraft draft)
    {
        float ratio = MoveSpaceScorer.GetRatioScoreFromStatisticalDistance(
            source.StatisticalDistance, (float)lowThreshold, (float)highThreshold);
        bool ignoreDirection = source.DirectionTendencyIgnored || draft.IgnoreDirection;
        return source with
        {
            RatioScore = ratio,
            PercentageScore = ratio * 100.0f,
            LowThreshold = (float)lowThreshold,
            HighThreshold = (float)highThreshold,
            AutoCorrelationThreshold = (float)ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.AutoCorrelationThreshold),
            DirectionImpactFactor = (float)ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor),
            DirectionTendencyIgnored = ignoreDirection,
            DirectionTendencyImpactOnScoreRatio = ignoreDirection
                ? 0.0f
                : Finite(source.DirectionTendencyImpactOnScoreRatio)
                    * (float)ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor),
            AutoCorrelationTime = draft.IgnoreAutocorrelation ? -6.0f : source.AutoCorrelationTime
        };
    }

    private static float ProjectDistancePercentage(float distance, double low, double high)
        => Math.Clamp(MoveSpaceScorer.GetRatioScoreFromStatisticalDistance(distance, (float)low, (float)high) * 100.0f, 0.0f, 100.0f);

    private static float PreviewAccuracy(MotionRecordingMoveScorePoint point)
        => string.IsNullOrWhiteSpace(point.Issue)
            ? MotionRecordingScoreMath.NormalizePercentage(point.AdjustedPercentageScore)
            : 0.0f;

    private static string CreateLabel(MotionRecordingMoveScorePoint point)
        => $"Recording {point.RecordingIndex + 1}, coach {point.CoachId}, move {point.MoveIndex}, beat {point.StartBeatLabel:0.##}";

    private static double Quantile(IReadOnlyList<double> sortedValues, double quantile)
    {
        double position = Math.Clamp(quantile, 0.0, 1.0) * (sortedValues.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sortedValues[lower]
            : sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * (position - lower));
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0.0f;

    private sealed class SeriesBuilder
    {
        public SeriesBuilder(ScoringAdjustmentSample sample)
            : this(sample.Label, sample.RecordingIndex, sample.CoachId, sample.MoveIndex, sample.SavedPercentageScore)
        {
        }

        public SeriesBuilder(MotionRecordingMoveScorePoint point, float savedAccuracy)
            : this(CreateLabel(point), point.RecordingIndex, point.CoachId, point.MoveIndex, savedAccuracy)
        {
        }

        private SeriesBuilder(string label, int recordingIndex, int coachId, int moveIndex, float savedAccuracy)
        {
            Label = label;
            RecordingIndex = recordingIndex;
            CoachId = coachId;
            MoveIndex = moveIndex;
            SavedAccuracy = savedAccuracy;
        }

        public string Label { get; }
        public int RecordingIndex { get; }
        public int CoachId { get; }
        public int MoveIndex { get; }
        public float SavedAccuracy { get; }
        public List<ScoringAdjustmentSweepPoint> Points { get; } = [];
    }
}
