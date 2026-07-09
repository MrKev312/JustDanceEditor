using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal static class ScoringAdjustmentPresentation
{
    public static string ParameterTitle(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => "Low threshold sweep",
            ScoringAdjustmentParameter.HighThreshold => "High threshold sweep",
            ScoringAdjustmentParameter.AutoCorrelationThreshold => "Autocorr sweep",
            ScoringAdjustmentParameter.DirectionImpactFactor => "Direction impact sweep",
            _ => "Parameter sweep"
        };

    public static string ParameterGuidance(
        ScoringAdjustmentParameter parameter,
        IReadOnlyList<ScoringAdjustmentSweepSeries>? series,
        bool ignored)
    {
        string hint = parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => "Low controls where distance starts reaching Perfect. Raise it when good takes narrowly miss Perfect; lower it when sloppy-but-close takes are too generous.",
            ScoringAdjustmentParameter.HighThreshold => "High controls where distance collapses to X. Raise it to forgive rough takes; lower it when wrong or weak takes need to fail sooner.",
            ScoringAdjustmentParameter.AutoCorrelationThreshold => "Autocorr controls when repeated shake-like motion is detected and capped. Lower values catch more takes; higher values let more takes through.",
            ScoringAdjustmentParameter.DirectionImpactFactor => "Direction impact scales the direction tendency returned by MoveSpace. Lower it to soften wrong-direction penalties; raise it to trust the direction signal more.",
            _ => "Use the sweep to compare loaded recordings across the selected range."
        };

        if (series == null)
            return hint + " Waiting for the current diagram to finish calculating.";
        if (series.Count == 0)
            return hint + " No loaded recording has a scored instance for this move yet.";

        List<float> candidateAverages = [.. series
            .SelectMany(static item => item.RangePoints)
            .GroupBy(static point => Math.Round(point.Value, 6))
            .Select(static group => group.Average(static point => point.Accuracy))];
        if (candidateAverages.Count == 0)
            return hint + " No range samples are available for this parameter yet.";

        float min = candidateAverages.Min();
        float max = candidateAverages.Max();
        if (max - min <= 0.1f)
        {
            return hint + (ignored
                ? " Flat line: this signal is currently bypassed."
                : " Flat line: the loaded samples do not respond differently inside this range.");
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} Sampled average range: {1:0.0}% to {2:0.0}% ({3:+0.0;-0.0;0.0}% spread).",
            hint,
            min,
            max,
            max - min);
    }

    public static (string Accuracy, string Delta) Statistics(
        IReadOnlyList<ScoringAdjustmentSweepSeries> series,
        double currentValue,
        bool useDefault)
    {
        if (series.Count == 0)
            return ("-", "-");

        List<float> values = [];
        foreach (ScoringAdjustmentSweepSeries item in series)
        {
            ScoringAdjustmentSweepPoint? point = useDefault
                ? item.DefaultPoint
                : Interpolate(item.RangePoints, currentValue);
            if (point != null)
                values.Add(point.Accuracy);
        }

        if (values.Count == 0)
            return ("-", "-");

        float current = values.Average();
        float saved = series.Average(static item => item.SavedAccuracy);
        return (
            current.ToString("0.0", CultureInfo.InvariantCulture) + "%",
            (current - saved).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%");
    }

    public static string Percent(double value)
        => (ScoringAdjustmentDraftMapper.ClampUnit(value) * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string ParameterRange(ScoringAdjustmentParameter parameter)
    {
        (double min, double max) = ScoringAdjustmentDraftMapper.Range(parameter);
        return string.Format(CultureInfo.InvariantCulture, "Slider range: {0:0.###} <= value <= {1:0.###}", min, max);
    }

    private static ScoringAdjustmentSweepPoint? Interpolate(
        IReadOnlyList<ScoringAdjustmentSweepPoint> points,
        double value)
    {
        if (points.Count == 0)
            return null;

        ScoringAdjustmentSweepPoint lower = points[0];
        ScoringAdjustmentSweepPoint upper = points[^1];
        foreach (ScoringAdjustmentSweepPoint point in points)
        {
            if (point.Value <= value)
                lower = point;
            if (point.Value >= value)
            {
                upper = point;
                break;
            }
        }

        if (Math.Abs(upper.Value - lower.Value) < 0.000001)
            return lower;

        double ratio = (value - lower.Value) / (upper.Value - lower.Value);
        return new(value, (float)(lower.Accuracy + ((upper.Accuracy - lower.Accuracy) * ratio)), false);
    }
}
