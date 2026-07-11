namespace JustDanceEditor.Scoring;

public enum MotionRecordingScoringProfile
{
    Raw,
    JDNow,
    UbiArt,
    JDNext
}

public enum MotionRecordingMoveFeedback
{
    X,
    Ok,
    Good,
    Super,
    Perfect,
    Yeah
}

public readonly record struct MotionRecordingScoreEvaluation(
    float PercentageScore,
    MotionRecordingMoveFeedback Feedback,
    float AddedScore);

public static class MotionRecordingScoreMath
{
    private const float MoveScoreBadRatio = 0.0f;
    private const float MoveScoreCharityOkRatio = 0.1f;
    private const float MoveScoreOkRatio = 0.25f;
    private const float MoveScorePerfectRatio = 0.75f;
    private const float MoveScoreMaxRatio = 1.0f;
    private const float JdNowPhoneScoreBoost = 0.1f;
    private const float JdNowCharityEnergyFactorFloor = 0.6f;
    private const float JdNowPerfectMalusEnergyFactorCeil = 0.4f;
    private const float JdNowShakeDetectedMaxScoreRatio = 0.4f;
    private const float JdNowDirectionMalusMultiplier = 0.5f;
    private const float JdNowNoMovePenaltyEnergyAmountCeil = 0.25f;
    private const float UbiArtPhoneScoreBoost = 0.03f;
    private const float UbiArtPositiveDirectionMultiplier = 0.1f;
    private const float JDNextPhoneScoreBoost = 0.09f;
    private const float JDNextPositiveDirectionMultiplier = 0.2f;
    private const float OfficialPerfectRatio = 0.9f;
    private const float OfficialSuperRatio = 0.75f;
    private const float OfficialGoldYeahRatio = 0.5f;
    private const float OfficialGoldMoveValue = 5.0f;
    private const float LegacyGoldMoveValue = 3.5f;

    public static MotionRecordingMoveFeedback GetFeedback(bool isGoldMove, float percentageScore)
    {
        if (isGoldMove)
            return percentageScore > 70.0f
                ? MotionRecordingMoveFeedback.Yeah
                : MotionRecordingMoveFeedback.X;

        if (percentageScore < 25.0f)
            return MotionRecordingMoveFeedback.X;
        if (percentageScore < 50.0f)
            return MotionRecordingMoveFeedback.Ok;
        if (percentageScore < 70.0f)
            return MotionRecordingMoveFeedback.Good;
        if (percentageScore < 80.0f)
            return MotionRecordingMoveFeedback.Super;
        return MotionRecordingMoveFeedback.Perfect;
    }

    public static MotionRecordingMoveFeedback GetFeedback(
        bool isGoldMove,
        float percentageScore,
        MotionRecordingScoringProfile profile)
    {
        return profile switch
        {
            MotionRecordingScoringProfile.UbiArt => GetOfficialFeedback(isGoldMove, percentageScore, MoveScoreCharityOkRatio),
            MotionRecordingScoringProfile.JDNext => GetOfficialFeedback(isGoldMove, percentageScore, 0.05f),
            _ => GetFeedback(isGoldMove, percentageScore)
        };
    }

    public static float NormalizePercentage(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0.0f;

        return Math.Clamp(value, 0.0f, 100.0f);
    }

    public static MoveScoringOptions ApplyScoringProfileDefaults(
        MoveScoringOptions options,
        MotionRecordingScoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(options);

        return profile == MotionRecordingScoringProfile.JDNext
            ? options with
            {
                DefaultLowThreshold = 1.0f,
                DefaultHighThreshold = 3.5f,
                DefaultAutoCorrelationThreshold = 0.7f,
                DefaultDirectionImpactFactor = 1.0f,
                SmoothingFrequency = 60.0f
            }
            : options with
            {
                DefaultLowThreshold = 1.0f,
                DefaultHighThreshold = 3.0f,
                DefaultAutoCorrelationThreshold = 0.7f,
                DefaultDirectionImpactFactor = 1.0f,
                SmoothingFrequency = 60.0f
            };
    }

    public static float GetDefaultGoldMoveValue(MotionRecordingScoringProfile profile)
        => profile is MotionRecordingScoringProfile.UbiArt or MotionRecordingScoringProfile.JDNext
            ? OfficialGoldMoveValue
            : LegacyGoldMoveValue;

    public static MotionRecordingScoreEvaluation EvaluateMove(
        bool isGoldMove,
        MoveSpaceScoreResult? moveSpace,
        float goldScoreValue,
        float moveScoreValue,
        MotionRecordingScoringProfile profile)
    {
        float percentageScore = moveSpace == null
            ? 0.0f
            : GetProfilePercentage(moveSpace, profile);
        MotionRecordingMoveFeedback feedback = GetFeedback(isGoldMove, percentageScore, profile);
        float addedScore = GetAddedScore(
            isGoldMove,
            feedback,
            percentageScore,
            goldScoreValue,
            moveScoreValue,
            profile);

        return new MotionRecordingScoreEvaluation(percentageScore, feedback, addedScore);
    }

    public static float GetProfilePercentage(
        MoveSpaceScoreResult moveSpace,
        MotionRecordingScoringProfile profile,
        float? ratioScoreOverride = null)
    {
        ArgumentNullException.ThrowIfNull(moveSpace);

        return profile switch
        {
            MotionRecordingScoringProfile.Raw => NormalizePercentage(NormalizeRatio(ratioScoreOverride ?? moveSpace.RatioScore) * 100.0f),
            MotionRecordingScoringProfile.JDNow => GetAdjustedPercentage(moveSpace, ratioScoreOverride),
            MotionRecordingScoringProfile.UbiArt => GetGameplayAdjustedPercentage(
                moveSpace,
                ratioScoreOverride,
                UbiArtPhoneScoreBoost,
                UbiArtPositiveDirectionMultiplier,
                MoveScoreCharityOkRatio,
                MoveScorePerfectRatio),
            MotionRecordingScoringProfile.JDNext => GetGameplayAdjustedPercentage(
                moveSpace,
                ratioScoreOverride,
                JDNextPhoneScoreBoost,
                JDNextPositiveDirectionMultiplier,
                0.05f,
                OfficialPerfectRatio),
            _ => NormalizePercentage(moveSpace.PercentageScore)
        };
    }

    public static float GetAdjustedPercentage(MoveSpaceScoreResult moveSpace, float? ratioScoreOverride = null)
    {
        float ratioScore = ratioScoreOverride.HasValue
            ? NormalizeRatio(ratioScoreOverride.Value)
            : NormalizeRatio(moveSpace.RatioScore);
        float adjustedScore = Math.Min(ratioScore + (ratioScore * JdNowPhoneScoreBoost), MoveScoreMaxRatio);
        float energyAmount = SanitizeFinite(moveSpace.EnergyAmount, 0.0f);
        float energyFactor = SanitizeFinite(moveSpace.EnergyFactor, 0.0f);
        float directionImpact = SanitizeFinite(moveSpace.DirectionTendencyImpactOnScoreRatio, 0.0f);
        bool charityBonusIsAllowed = true;

        if (adjustedScore < MoveScoreOkRatio)
        {
            adjustedScore = MoveScoreBadRatio;
        }
        else if (energyAmount < JdNowNoMovePenaltyEnergyAmountCeil)
        {
            adjustedScore = MoveScoreBadRatio;
            charityBonusIsAllowed = false;
        }
        else if (moveSpace.AutoCorrelationTime > 0.0f)
        {
            adjustedScore = Math.Min(adjustedScore, JdNowShakeDetectedMaxScoreRatio);
        }
        else if (!moveSpace.DirectionTendencyIgnored)
        {
            if (directionImpact < 0.0f)
            {
                adjustedScore += directionImpact * JdNowDirectionMalusMultiplier;
                if (adjustedScore < MoveScoreOkRatio)
                    adjustedScore = MoveScoreBadRatio;
            }
            else if (directionImpact > 0.0f)
            {
                adjustedScore = Math.Min(adjustedScore + (directionImpact * JdNowPhoneScoreBoost), MoveScoreMaxRatio);
            }
        }

        if (adjustedScore < MoveScoreCharityOkRatio)
        {
            if (charityBonusIsAllowed && energyFactor >= JdNowCharityEnergyFactorFloor)
                adjustedScore = MoveScoreCharityOkRatio;
        }
        else if (adjustedScore >= MoveScorePerfectRatio && energyFactor < JdNowPerfectMalusEnergyFactorCeil)
        {
            adjustedScore = MoveScorePerfectRatio - 0.01f;
        }

        return NormalizePercentage(adjustedScore * 100.0f);
    }

    public static float GetAddedScore(
        bool isGoldMove,
        MotionRecordingMoveFeedback feedback,
        float percentageScore,
        float goldScoreValue,
        float moveScoreValue)
    {
        if (feedback == MotionRecordingMoveFeedback.X)
            return 0.0f;

        float scoreValue = isGoldMove && feedback == MotionRecordingMoveFeedback.Yeah
            ? goldScoreValue
            : moveScoreValue;
        return scoreValue * (percentageScore / 100.0f);
    }

    public static float GetAddedScore(
        bool isGoldMove,
        MotionRecordingMoveFeedback feedback,
        float percentageScore,
        float goldScoreValue,
        float moveScoreValue,
        MotionRecordingScoringProfile profile)
    {
        if (profile is not (MotionRecordingScoringProfile.UbiArt or MotionRecordingScoringProfile.JDNext))
            return GetAddedScore(isGoldMove, feedback, percentageScore, goldScoreValue, moveScoreValue);

        if (feedback == MotionRecordingMoveFeedback.X)
            return 0.0f;

        if (isGoldMove)
            return feedback == MotionRecordingMoveFeedback.Yeah ? goldScoreValue : 0.0f;

        return moveScoreValue * (percentageScore / 100.0f);
    }

    public static (float GoldScoreValue, float MoveScoreValue) GetScoreValues(
        int standardMoveCount,
        int goldMoveCount,
        float songScoreMaxScore,
        float goldMoveValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(standardMoveCount);
        ArgumentOutOfRangeException.ThrowIfNegative(goldMoveCount);

        float denominator = (goldMoveValue * goldMoveCount) + standardMoveCount;
        if (denominator == 0.0f)
            return (0.0f, 0.0f);

        float moveValue = songScoreMaxScore / denominator;
        return (moveValue * goldMoveValue, moveValue);
    }

    private static float GetGameplayAdjustedPercentage(
        MoveSpaceScoreResult moveSpace,
        float? ratioScoreOverride,
        float phoneScoreBoost,
        float positiveDirectionMultiplier,
        float charityOkRatio,
        float perfectMalusTriggerRatio)
    {
        float ratioScore = ratioScoreOverride.HasValue
            ? NormalizeRatio(ratioScoreOverride.Value)
            : NormalizeRatio(moveSpace.RatioScore);
        float adjustedScore = Math.Min(ratioScore + (ratioScore * phoneScoreBoost), MoveScoreMaxRatio);
        float energyAmount = SanitizeFinite(moveSpace.EnergyAmount, 0.0f);
        float energyFactor = SanitizeFinite(moveSpace.EnergyFactor, 0.0f);
        float directionImpact = SanitizeFinite(moveSpace.DirectionTendencyImpactOnScoreRatio, 0.0f);
        bool charityBonusIsAllowed = true;

        if (adjustedScore < MoveScoreOkRatio)
        {
            adjustedScore = MoveScoreBadRatio;
        }
        else if (energyAmount < JdNowNoMovePenaltyEnergyAmountCeil)
        {
            adjustedScore = MoveScoreBadRatio;
            charityBonusIsAllowed = false;
        }
        else if (moveSpace.AutoCorrelationTime > 0.0f)
        {
            adjustedScore = Math.Min(adjustedScore, JdNowShakeDetectedMaxScoreRatio);
        }
        else if (!moveSpace.DirectionTendencyIgnored)
        {
            if (directionImpact < 0.0f)
            {
                adjustedScore += directionImpact * JdNowDirectionMalusMultiplier;
                if (adjustedScore < MoveScoreOkRatio)
                    adjustedScore = MoveScoreBadRatio;
            }
            else if (directionImpact > 0.0f)
            {
                adjustedScore = Math.Min(adjustedScore + (directionImpact * positiveDirectionMultiplier), MoveScoreMaxRatio);
            }
        }

        if (adjustedScore < charityOkRatio)
        {
            if (charityBonusIsAllowed && energyFactor >= JdNowCharityEnergyFactorFloor)
                adjustedScore = charityOkRatio;
        }
        else if (adjustedScore >= perfectMalusTriggerRatio && energyFactor < JdNowPerfectMalusEnergyFactorCeil)
        {
            adjustedScore = OfficialSuperRatio - 0.01f;
        }

        return NormalizePercentage(adjustedScore * 100.0f);
    }

    private static MotionRecordingMoveFeedback GetOfficialFeedback(
        bool isGoldMove,
        float percentageScore,
        float charityOkRatio)
    {
        float ratioScore = NormalizePercentage(percentageScore) / 100.0f;
        if (isGoldMove)
            return ratioScore >= OfficialGoldYeahRatio
                ? MotionRecordingMoveFeedback.Yeah
                : MotionRecordingMoveFeedback.X;

        if (ratioScore >= OfficialPerfectRatio)
            return MotionRecordingMoveFeedback.Perfect;
        if (ratioScore >= OfficialSuperRatio)
            return MotionRecordingMoveFeedback.Super;
        if (ratioScore >= OfficialGoldYeahRatio)
            return MotionRecordingMoveFeedback.Good;
        if (ratioScore >= MoveScoreOkRatio || Math.Abs(ratioScore - charityOkRatio) < 0.0001f)
            return MotionRecordingMoveFeedback.Ok;
        return MotionRecordingMoveFeedback.X;
    }

    private static float NormalizeRatio(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0.0f;

        return Math.Clamp(value, 0.0f, 1.0f);
    }

    private static float SanitizeFinite(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
}
