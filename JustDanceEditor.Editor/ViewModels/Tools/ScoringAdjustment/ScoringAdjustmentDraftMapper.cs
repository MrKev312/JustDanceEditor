using JustDanceEditor.Scoring;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal static class ScoringAdjustmentDraftMapper
{
    private const uint IgnoreDirectionFlag = 1U << 0;
    private const uint IgnoreAutoCorrelationFlag = 1U << 1;
    private const int SweepSampleCount = 25;

    public static ScoringAdjustmentDraft FromHeader(MotionClassifierHeader header)
        => new(
            SliderValue(header.LowThreshold, ScoringAdjustmentParameter.LowThreshold),
            IsDefault(header.LowThreshold),
            SliderValue(header.HighThreshold, ScoringAdjustmentParameter.HighThreshold),
            IsDefault(header.HighThreshold),
            SliderValue(header.AutoCorrelationThreshold, ScoringAdjustmentParameter.AutoCorrelationThreshold),
            IsDefault(header.AutoCorrelationThreshold),
            SliderValue(header.DirectionImpactFactor, ScoringAdjustmentParameter.DirectionImpactFactor),
            IsDefault(header.DirectionImpactFactor),
            (header.CustomizationBitField & IgnoreDirectionFlag) != 0,
            (header.CustomizationBitField & IgnoreAutoCorrelationFlag) != 0);

    public static byte[] Write(byte[] source, ScoringAdjustmentDraft draft)
        => MotionClassifierHeaderEditor.UpdateHeader(source, CreateHeaderUpdate(draft));

    public static MotionClassifierHeaderUpdate CreateHeaderUpdate(ScoringAdjustmentDraft draft)
        => new()
        {
            LowThreshold = HeaderValue(draft, ScoringAdjustmentParameter.LowThreshold),
            HighThreshold = HeaderValue(draft, ScoringAdjustmentParameter.HighThreshold),
            AutoCorrelationThreshold = HeaderValue(draft, ScoringAdjustmentParameter.AutoCorrelationThreshold),
            DirectionImpactFactor = HeaderValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor),
            CustomizationBitField = BuildCustomizationBitField(draft)
        };

    public static ScoringAdjustmentDraft WithCandidate(
        ScoringAdjustmentDraft draft,
        ScoringAdjustmentParameter parameter,
        ScoringAdjustmentCandidate candidate)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => draft with { LowThreshold = candidate.Value, LowThresholdDefault = candidate.IsDefault },
            ScoringAdjustmentParameter.HighThreshold => draft with { HighThreshold = candidate.Value, HighThresholdDefault = candidate.IsDefault },
            ScoringAdjustmentParameter.AutoCorrelationThreshold => draft with { AutoCorrelationThreshold = candidate.Value, AutoCorrelationThresholdDefault = candidate.IsDefault },
            ScoringAdjustmentParameter.DirectionImpactFactor => draft with { DirectionImpactFactor = candidate.Value, DirectionImpactFactorDefault = candidate.IsDefault },
            _ => draft
        };

    public static ScoringAdjustmentDraft ForPrimitiveProjection(ScoringAdjustmentDraft draft)
        => draft with
        {
            DirectionImpactFactor = DefaultValue(ScoringAdjustmentParameter.DirectionImpactFactor),
            DirectionImpactFactorDefault = false,
            IgnoreDirection = false,
            IgnoreAutocorrelation = false
        };

    public static bool IsEquivalent(ScoringAdjustmentDraft left, ScoringAdjustmentDraft right)
        => Math.Abs(left.LowThreshold - right.LowThreshold) <= 0.000001
            && left.LowThresholdDefault == right.LowThresholdDefault
            && Math.Abs(left.HighThreshold - right.HighThreshold) <= 0.000001
            && left.HighThresholdDefault == right.HighThresholdDefault
            && Math.Abs(left.AutoCorrelationThreshold - right.AutoCorrelationThreshold) <= 0.000001
            && left.AutoCorrelationThresholdDefault == right.AutoCorrelationThresholdDefault
            && Math.Abs(left.DirectionImpactFactor - right.DirectionImpactFactor) <= 0.000001
            && left.DirectionImpactFactorDefault == right.DirectionImpactFactorDefault
            && left.IgnoreDirection == right.IgnoreDirection
            && left.IgnoreAutocorrelation == right.IgnoreAutocorrelation;

    public static double GetValue(ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => draft.LowThreshold,
            ScoringAdjustmentParameter.HighThreshold => draft.HighThreshold,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => draft.AutoCorrelationThreshold,
            ScoringAdjustmentParameter.DirectionImpactFactor => draft.DirectionImpactFactor,
            _ => DefaultValue(parameter)
        };

    public static bool UsesDefault(ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => draft.LowThresholdDefault,
            ScoringAdjustmentParameter.HighThreshold => draft.HighThresholdDefault,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => draft.AutoCorrelationThresholdDefault,
            ScoringAdjustmentParameter.DirectionImpactFactor => draft.DirectionImpactFactorDefault,
            _ => false
        };

    public static double EffectiveValue(ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter)
        => UsesDefault(draft, parameter) ? DefaultValue(parameter) : GetValue(draft, parameter);

    public static float HeaderValue(ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter)
        => UsesDefault(draft, parameter) ? -1.0f : (float)Clamp(parameter, GetValue(draft, parameter));

    public static float HeaderValue(MotionClassifierHeader header, ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => header.LowThreshold,
            ScoringAdjustmentParameter.HighThreshold => header.HighThreshold,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => header.AutoCorrelationThreshold,
            ScoringAdjustmentParameter.DirectionImpactFactor => header.DirectionImpactFactor,
            _ => -1.0f
        };

    public static double Clamp(ScoringAdjustmentParameter parameter, double value)
    {
        (double min, double max) = Range(parameter);
        return double.IsFinite(value) ? Math.Clamp(value, min, max) : DefaultValue(parameter);
    }

    public static (double Min, double Max) Range(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => (0.4, 1.4),
            ScoringAdjustmentParameter.HighThreshold => (1.5, 6.0),
            ScoringAdjustmentParameter.AutoCorrelationThreshold => (0.5, 1.3),
            ScoringAdjustmentParameter.DirectionImpactFactor => (0.0, 1.0),
            _ => (0.0, 1.0)
        };

    public static double DefaultValue(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => 1.0,
            ScoringAdjustmentParameter.HighThreshold => 3.0,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => 1.0,
            ScoringAdjustmentParameter.DirectionImpactFactor => 1.0,
            _ => 0.0
        };

    public static IEnumerable<ScoringAdjustmentCandidate> Candidates(ScoringAdjustmentParameter parameter)
    {
        yield return new(DefaultValue(parameter), IsDefault: true);
        (double min, double max) = Range(parameter);
        for (int index = 0; index < SweepSampleCount; index++)
            yield return new(min + ((max - min) * index / (SweepSampleCount - 1)), IsDefault: false);
    }

    public static bool IsDefault(float value) => Math.Abs(value + 1.0f) < 0.000001f;

    public static double SliderValue(float headerValue, ScoringAdjustmentParameter parameter)
        => IsDefault(headerValue) ? DefaultValue(parameter) : Clamp(parameter, headerValue);

    public static double SensitivityFromAutoCorrelation(double threshold)
    {
        (double min, double max) = Range(ScoringAdjustmentParameter.AutoCorrelationThreshold);
        return ClampUnit((max - threshold) / (max - min));
    }

    public static double AutoCorrelationFromSensitivity(double sensitivity)
    {
        (double min, double max) = Range(ScoringAdjustmentParameter.AutoCorrelationThreshold);
        return max + (ClampUnit(sensitivity) * (min - max));
    }

    public static double ClampUnit(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;

    private static uint BuildCustomizationBitField(ScoringAdjustmentDraft draft)
        => (draft.IgnoreDirection ? IgnoreDirectionFlag : 0U)
            | (draft.IgnoreAutocorrelation ? IgnoreAutoCorrelationFlag : 0U);
}
