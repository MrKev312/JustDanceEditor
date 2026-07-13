using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Scoring;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public enum ScoringAdjustmentParameter
{
    LowThreshold,
    HighThreshold,
    AutoCorrelationThreshold,
    DirectionImpactFactor
}

public sealed record ScoringAdjustmentSweepPoint(double Value, float Accuracy, bool IsDefaultValue);

public sealed record ScoringAdjustmentSweepSeries(
    string Label,
    int SeriesIndex,
    int RecordingIndex,
    int CoachId,
    int MoveIndex,
    float SavedAccuracy,
    IReadOnlyList<ScoringAdjustmentSweepPoint> Points)
{
    public ScoringAdjustmentSweepPoint? DefaultPoint => Points.FirstOrDefault(static point => point.IsDefaultValue);
    public IReadOnlyList<ScoringAdjustmentSweepPoint> RangePoints => [.. Points.Where(static point => !point.IsDefaultValue).OrderBy(static point => point.Value)];
}

public sealed record ScoringAdjustmentCurvePoint(float StatisticalDistance, float PercentageScore);

public sealed record ScoringAdjustmentCurveSample(
    string Label,
    int SeriesIndex,
    int RecordingIndex,
    int CoachId,
    int MoveIndex,
    float StatisticalDistance,
    float PercentageScore);

public sealed record ScoringAdjustmentCurve(
    IReadOnlyList<ScoringAdjustmentCurvePoint> Points,
    IReadOnlyList<ScoringAdjustmentCurveSample> Samples,
    int CandidateCount,
    int RecordingCount,
    int MoveInstanceCount,
    int ScoredMoveCount);

internal sealed record ScoringAdjustmentDraft(
    double LowThreshold,
    bool LowThresholdDefault,
    double HighThreshold,
    bool HighThresholdDefault,
    double AutoCorrelationThreshold,
    bool AutoCorrelationThresholdDefault,
    double DirectionImpactFactor,
    bool DirectionImpactFactorDefault,
    bool IgnoreDirection,
    bool IgnoreAutocorrelation);

internal readonly record struct ScoringAdjustmentCandidate(double Value, bool IsDefault);

internal readonly record struct ScoringMoveKey(int RecordingIndex, int CoachId, int MoveIndex)
{
    public static ScoringMoveKey From(MotionRecordingMoveScorePoint point)
        => new(point.RecordingIndex, point.CoachId, point.MoveIndex);
}

internal sealed record ScoringAdjustmentSample(
    ScoringMoveKey Key,
    string Label,
    int RecordingIndex,
    int CoachId,
    int MoveIndex,
    float SavedPercentageScore,
    MoveSpaceScoreResult MoveSpace);

internal sealed record ScoringAdjustmentRecommendation(
    double LowThreshold,
    double HighThreshold,
    double DistanceMin,
    double DistanceMedian,
    double DistanceMax,
    double AutoCorrelationThreshold,
    double AutoCorrelationSensitivity,
    int AutoCorrelationOccurrenceCount,
    double DirectionSensitivity,
    double DirectionNegativeImpactSum,
    int SampleCount);

internal sealed record ScoringAdjustmentPreviewResult(
    ScoringAdjustmentDraft Draft,
    IReadOnlyDictionary<ScoringAdjustmentParameter, IReadOnlyList<ScoringAdjustmentSweepSeries>> Sweeps,
    IReadOnlyList<ScoringAdjustmentSample> Samples,
    ScoringAdjustmentRecommendation? Recommendation,
    int CandidateCount,
    int RecordingCount,
    int MoveInstanceCount,
    int ScoredMoveCount);