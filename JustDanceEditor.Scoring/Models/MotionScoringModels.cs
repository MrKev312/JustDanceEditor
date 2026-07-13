namespace JustDanceEditor.Scoring;

public enum DanceMoveFeedback
{
    Miss,
    Ok,
    Good,
    Super,
    Perfect,
    Yeah,
    MissYeah
}

public enum SongScoringMethod
{
    LegacyMoveSpace,
    JustDance
}

public enum MotionSampleFeedMode
{
    Direct,
    ProgressRatioDirect,
    LegacyToolInterpolation
}

public sealed record MoveScoringOptions
{
    public float DefaultLowThreshold { get; init; } = 1.0f;
    public float DefaultHighThreshold { get; init; } = 3.0f;
    public float DefaultAutoCorrelationThreshold { get; init; } = 0.7f;
    public float DefaultDirectionImpactFactor { get; init; } = 1.0f;
    public float SmoothingFrequency { get; init; } = 60.0f;
    public float AccelSaturationValue { get; init; } = 3.4f;
    public float EnergyAmountAccelDevNormRatio { get; init; } = 0.1f;
    public float EnergyFactorAccelDevNormRatio { get; init; } = 0.667f;
    public float AutoCorrelationStepTimeShift { get; init; } = 0.02f;
    public float AutoCorrelationMaxTimeShift { get; init; } = 0.5f;
    public MotionSampleFeedMode FeedMode { get; init; } = MotionSampleFeedMode.Direct;
}

public sealed record MoveScoreRequest
{
    public required string MoveName { get; init; }
    public required byte[] ClassifierBytes { get; init; }
    public required float Duration { get; init; }
    public IReadOnlyList<MotionSample> Samples { get; init; } = [];
    public MoveScoringOptions Options { get; init; } = new();
}

public sealed record MoveSpaceScoreResult(
    string MoveName,
    float StatisticalDistance,
    float RatioScore,
    float PercentageScore,
    float EnergyAmount,
    float EnergyFactor,
    float AutoCorrelationTime,
    bool DirectionTendencyIgnored,
    float DirectionTendencyImpactOnScoreRatio,
    float LowThreshold,
    float HighThreshold,
    float AutoCorrelationThreshold,
    float DirectionImpactFactor);

public sealed record SongMoveScoringInput
{
    public required string MoveName { get; init; }
    public required byte[] ClassifierBytes { get; init; }
    public float StartTime { get; init; }
    public required float Duration { get; init; }
    public bool IsGoldMove { get; init; }
    public IReadOnlyList<MotionSample> Samples { get; init; } = [];
}

public sealed record SongScoringOptions
{
    public MoveScoringOptions MoveSpaceOptions { get; init; } = new();
    public float SongScoreMaxScore { get; init; } = 13333.0f;
    public float GoldMoveValue { get; init; } = 5.0f;
    public float LegacyToolGoldMoveValue { get; init; } = 3.5f;
    public float OnFireFactor { get; init; } = 1.5f;
    public int OnFireThreshold { get; init; } = 10;
    public int OnFireMax { get; init; } = 20;
    public float PerfectFeedbackMinScore { get; init; } = 0.8f;
    public float NoMovePenaltyEnergyAmountCeil { get; init; } = 0.3f;
    public float CharityEnergyFactorFloor { get; init; } = 0.6f;
    public float PerfectMalusEnergyFactorCeil { get; init; } = 0.4f;
    public float PhoneNoMovePenaltyEnergyAmountCeil { get; init; } = 0.25f;
    public float PhoneShakeDetectedMaxScoreRatio { get; init; } = 0.1f;
    public float PhoneDirectionMalusMultiplier { get; init; } = 1.0f;
    public bool PhoneScoring { get; init; }
    public bool KidsMode { get; init; }
    public float KidsModeDecreasingScoreRatioBoost { get; init; }
    public float KidsModeCharityOkScoreRatio { get; init; }
    public float KidsModeCharityOkEnergyAmountFactor { get; init; } = 1.0f;
}

public sealed record SongScoringRequest
{
    public IReadOnlyList<SongMoveScoringInput> Moves { get; init; } = [];
    public IReadOnlyList<MotionSample> TimelineSamples { get; init; } = [];
    public SongScoringOptions Options { get; init; } = new();
}

public sealed record SongMoveScore(
    string MoveName,
    bool IsGoldMove,
    DanceMoveFeedback Feedback,
    MoveSpaceScoreResult MoveSpace,
    float RawPercentageScore,
    float FinalMoveScoreRatio,
    float EnergyAmount,
    float EnergyFactor,
    float AddedScore,
    float TotalScore);

public sealed record SongScoringResult(
    SongScoringMethod Method,
    IReadOnlyList<SongMoveScore> Moves,
    float TotalScore,
    float TotalScoreRatio);

internal sealed record MotionClassifier(
    string SongName,
    string MoveName,
    string MeasureSetName,
    float Duration,
    float LowThreshold,
    float HighThreshold,
    float AutoCorrelationThreshold,
    float DirectionImpactFactor,
    ulong MeasureSetBitfield,
    uint CustomizationBitField,
    int ScoringAlgorithmType,
    float[] Means,
    float[] InvertedCovariances,
    float[] EnergyMeans,
    MotionClassifierFormatVersion FormatVersion,
    uint SubClassifiersCount,
    bool IsBigEndian);