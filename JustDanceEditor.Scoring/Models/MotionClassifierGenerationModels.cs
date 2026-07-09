namespace JustDanceEditor.Scoring;

public sealed record MotionClassifierGenerationOptions
{
    public float LowThreshold { get; init; } = 1.0f;
    public float HighThreshold { get; init; } = 3.0f;
    public float AutoCorrelationThreshold { get; init; } = 1.0f;
    public float DirectionImpactFactor { get; init; } = -1.0f;
    public uint CustomizationBitField { get; init; } = 2;
    public uint ClassifierFormatVersion { get; init; } = 7;
    public float AccelSaturationValue { get; init; } = 3.4f;
    public float SmoothingFrequency { get; init; } = 60.0f;
}

public sealed record MotionSample(float Time, float AccX, float AccY, float AccZ);

public sealed record MotionExample(float Duration, IReadOnlyList<MotionSample> Samples);

public sealed record MotionClassifierBuildRequest
{
    public required string SongName { get; init; }
    public required string MoveName { get; init; }
    public IReadOnlyList<MotionExample> Examples { get; init; } = [];
    public MotionClassifierGenerationOptions Options { get; init; } = new();
}

internal sealed record MotionObservation(
    List<float> Measures,
    List<float> EnergyMeasures);

internal sealed record MotionModelData(
    string SongName,
    string ModelName,
    string MeasureSetName,
    float Duration,
    List<MotionObservation> Observations);

internal sealed record MotionClassifierData(
    string SongName,
    string ModelName,
    string MeasureSetName,
    float Duration,
    double[] Means,
    double[] Variances,
    double[] EnergyMeans);

internal readonly record struct MotionMeasureResult(byte MeasureId, byte PartPosition, float Value);

internal readonly record struct MotionAutoCorrelationSample(float Time, float AccelNorm);

internal readonly record struct ProgressMotionSample(float ProgressRatio, float AccX, float AccY, float AccZ);

internal sealed record MotionAnalysisResult(
    List<MotionMeasureResult> Measures,
    List<float> EnergyMeasures,
    List<MotionAutoCorrelationSample> AutoCorrelationSamples);
