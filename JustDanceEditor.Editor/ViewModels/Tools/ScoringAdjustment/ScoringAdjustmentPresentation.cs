using System.Globalization;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal static class ScoringAdjustmentPresentation
{
    public static string ParameterTitle(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => "Perfect distance sweep",
            ScoringAdjustmentParameter.HighThreshold => "Fail distance sweep",
            ScoringAdjustmentParameter.AutoCorrelationThreshold => "Shake sensitivity sweep",
            ScoringAdjustmentParameter.DirectionImpactFactor => "Direction sensitivity sweep",
            _ => "Parameter sweep"
        };

    public static string ParameterGuidance(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => "Raise Perfect distance when good takes narrowly miss Perfect, or lower it when loose takes score too generously.",
            ScoringAdjustmentParameter.HighThreshold => "Raise Fail distance to forgive rough takes, or lower it when wrong or weak takes should fail sooner.",
            ScoringAdjustmentParameter.AutoCorrelationThreshold => "Raise Shake sensitivity for stronger shake filtering, or lower it to be more forgiving.",
            ScoringAdjustmentParameter.DirectionImpactFactor => "Raise Direction sensitivity to trust the direction signal more, or lower it to soften wrong-direction penalties.",
            _ => "Use the sweep to compare the selected setting."
        };

    public static string Percent(double value)
        => (ScoringAdjustmentDraftMapper.ClampUnit(value) * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
}