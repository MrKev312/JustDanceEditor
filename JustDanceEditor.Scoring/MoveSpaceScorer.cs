namespace JustDanceEditor.Scoring;

public sealed class MoveSpaceScorer
{
    private const byte AxDevAvgDirNp = 50;
    private const byte AyDevAvgDirNp = 51;
    private const byte AzDevAvgDirNp = 52;
    private const uint IgnorePartsDirectionFlag = 1U << 0;
    private const uint IgnoreAutoCorrelationFlag = 1U << 1;

    private const float LowThresholdMin = 0.4f;
    private const float LowThresholdMax = 1.4f;
    private const float HighThresholdMin = 1.5f;
    private const float HighThresholdMax = 6.0f;
    private const float AutoCorrelationThresholdMin = 0.5f;
    private const float AutoCorrelationThresholdMax = 1.3f;
    private const float DirectionImpactFactorMin = 0.0f;
    private const float DirectionImpactFactorMax = 1.0f;

    public MoveSpaceScoreResult ScoreMove(MoveScoreRequest request)
    {
        MotionClassifier classifier = MotionClassifierReader.Read(request.ClassifierBytes);
        MoveScoringOptions options = request.Options;
        MoveRuntimeSettings runtime = ResolveRuntimeSettings(classifier, options);
        MotionAnalysisResult analysis = Analyze(request, classifier, options);

        float distance = SanitizeFinite(ComputeStatisticalDistance(classifier, analysis), -1.0f);
        float ratioScore = GetRatioScoreFromStatisticalDistance(distance, runtime.LowThreshold, runtime.HighThreshold);
        float energyAmount = SanitizeFinite(ComputeEnergyAmount(analysis.EnergyMeasures, options.EnergyAmountAccelDevNormRatio), 0.0f);
        float energyFactor = SanitizeFinite(ComputeEnergyFactor(analysis.EnergyMeasures, classifier.EnergyMeans, options.EnergyFactorAccelDevNormRatio), 0.0f);
        float autoCorrelationTime = SanitizeFinite(ComputeAutoCorrelationValidationTime(analysis.AutoCorrelationSamples, runtime.AutoCorrelationThreshold, runtime.CustomizationBitField, options), -9.0f);
        (bool directionIgnored, float directionImpact) = ComputeDirectionTendencyImpact(classifier, analysis, runtime);
        if (float.IsNaN(directionImpact) || float.IsInfinity(directionImpact))
        {
            directionIgnored = true;
            directionImpact = 0.0f;
        }

        return new MoveSpaceScoreResult(
            request.MoveName,
            distance,
            ratioScore,
            100.0f * ratioScore,
            energyAmount,
            energyFactor,
            autoCorrelationTime,
            directionIgnored,
            directionImpact,
            runtime.LowThreshold,
            runtime.HighThreshold,
            runtime.AutoCorrelationThreshold,
            runtime.DirectionImpactFactor);
    }

    public static float GetRatioScoreFromStatisticalDistance(float statisticalDistance, float lowThreshold, float highThreshold)
    {
        if (lowThreshold == -1.0f
            || highThreshold == -1.0f
            || statisticalDistance < 0.0f
            || float.IsNaN(statisticalDistance)
            || float.IsInfinity(statisticalDistance))
        {
            return 0.0f;
        }

        float ratioScore = (statisticalDistance - highThreshold) / (lowThreshold - highThreshold);
        return Clamp(ratioScore, 0.0f, 1.0f);
    }

    private static MotionAnalysisResult Analyze(MoveScoreRequest request, MotionClassifier classifier, MoveScoringOptions options)
    {
        MoveSignalAnalyzer analyzer = new(options.SmoothingFrequency, classifier.FormatVersion);
        if (options.FeedMode == MotionSampleFeedMode.LegacyToolInterpolation)
        {
            List<ProgressMotionSample> progressSamples = BuildLegacyToolProgressSamples(request.Samples, request.Duration);
            return analyzer.AnalyzeProgressSamples(progressSamples, classifier.Duration, request.Duration, options.AccelSaturationValue);
        }

        if (options.FeedMode == MotionSampleFeedMode.ProgressRatioDirect)
        {
            List<ProgressMotionSample> progressSamples = [.. request.Samples.Select(static sample => new ProgressMotionSample(sample.Time, sample.AccX, sample.AccY, sample.AccZ))];
            return analyzer.AnalyzeProgressSamples(progressSamples, classifier.Duration, request.Duration, options.AccelSaturationValue);
        }

        return analyzer.AnalyzeDetailed(request.Samples, classifier.Duration, request.Duration, options.AccelSaturationValue);
    }

    private static List<ProgressMotionSample> BuildLegacyToolProgressSamples(IReadOnlyList<MotionSample> samples, float duration)
    {
        List<ProgressMotionSample> result = [];

        for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
        {
            if (sampleIndex == 0)
                continue;

            MotionSample sample = samples[sampleIndex];
            float previousRatio = samples[sampleIndex - 1].Time / duration;
            float currentRatio = sample.Time / duration;
            float step = (currentRatio - previousRatio) / sampleIndex;

            for (int i = 0; i < sampleIndex; i++)
            {
                float ratio = Clamp(currentRatio - (step * (sampleIndex - (i + 1))), 0.0f, 1.0f);
                result.Add(new ProgressMotionSample(ratio, sample.AccX, sample.AccY, sample.AccZ));
            }
        }

        return result;
    }

    private static MoveRuntimeSettings ResolveRuntimeSettings(MotionClassifier classifier, MoveScoringOptions options)
    {
        float lowThreshold = classifier.LowThreshold == -1.0f ? options.DefaultLowThreshold : classifier.LowThreshold;
        lowThreshold = Clamp(lowThreshold, LowThresholdMin, LowThresholdMax);

        float highThreshold = classifier.HighThreshold == -1.0f ? options.DefaultHighThreshold : classifier.HighThreshold;
        highThreshold = Clamp(highThreshold, HighThresholdMin, HighThresholdMax);

        float autoCorrelationThreshold = classifier.AutoCorrelationThreshold == -1.0f
            ? options.DefaultAutoCorrelationThreshold
            : classifier.AutoCorrelationThreshold;
        autoCorrelationThreshold = Clamp(autoCorrelationThreshold, AutoCorrelationThresholdMin, AutoCorrelationThresholdMax);

        uint customizationBitField = classifier.CustomizationBitField;
        if (autoCorrelationThreshold == AutoCorrelationThresholdMax)
            customizationBitField |= IgnoreAutoCorrelationFlag;

        float directionImpactFactor = classifier.DirectionImpactFactor == -1.0f
            ? options.DefaultDirectionImpactFactor
            : classifier.DirectionImpactFactor;
        directionImpactFactor = Clamp(directionImpactFactor, DirectionImpactFactorMin, DirectionImpactFactorMax);
        if (directionImpactFactor == DirectionImpactFactorMin)
            customizationBitField |= IgnorePartsDirectionFlag;

        return new MoveRuntimeSettings(
            lowThreshold,
            highThreshold,
            autoCorrelationThreshold,
            directionImpactFactor,
            customizationBitField);
    }

    private static float ComputeStatisticalDistance(MotionClassifier classifier, MotionAnalysisResult analysis)
    {
        if (classifier.ScoringAlgorithmType == 0)
            return -1.0f;

        if (classifier.ScoringAlgorithmType > 0)
            return ComputeNaiveBayesStatisticalDistance(classifier, analysis);

        return ComputeMahalanobisStatisticalDistance(classifier, analysis);
    }

    private static float ComputeNaiveBayesStatisticalDistance(MotionClassifier classifier, MotionAnalysisResult analysis)
    {
        float sqrStatisticalDistance = 0.0f;
        int measuresUsedForScoringCount = 0;

        int index = 0;
        foreach (MotionMeasureResult measure in analysis.Measures)
        {
            if (!IsDirectionMeasure(measure.MeasureId))
            {
                float deviation = measure.Value - classifier.Means[index];
                sqrStatisticalDistance += deviation * deviation * classifier.InvertedCovariances[index];
                measuresUsedForScoringCount++;
            }

            index++;
        }

        return measuresUsedForScoringCount == 0
            ? -1.0f
            : MathF.Sqrt(sqrStatisticalDistance / measuresUsedForScoringCount);
    }

    private static float ComputeMahalanobisStatisticalDistance(MotionClassifier classifier, MotionAnalysisResult analysis)
    {
        float[] deviations = new float[analysis.Measures.Count];
        int measuresUsedForScoringCount = 0;

        for (int i = 0; i < analysis.Measures.Count; i++)
        {
            MotionMeasureResult measure = analysis.Measures[i];
            if (IsDirectionMeasure(measure.MeasureId))
            {
                deviations[i] = 0.0f;
                continue;
            }

            deviations[i] = measure.Value - classifier.Means[i];
            measuresUsedForScoringCount++;
        }

        float sqrStatisticalDistance = 0.0f;
        int covarianceIndex = 0;
        for (int row = 0; row < deviations.Length; row++)
        {
            for (int col = 0; col < deviations.Length; col++)
            {
                if (col < row)
                    continue;

                float value = deviations[row] * deviations[col] * classifier.InvertedCovariances[covarianceIndex];
                if (col > row)
                    value *= 2.0f;

                sqrStatisticalDistance += value;
                covarianceIndex++;
            }
        }

        return measuresUsedForScoringCount == 0
            ? -1.0f
            : MathF.Sqrt(sqrStatisticalDistance / measuresUsedForScoringCount);
    }

    private static float ComputeEnergyAmount(IReadOnlyList<float> energyMeans, float accelDevNormOverAccelNormUseRatio)
    {
        if (energyMeans.Count < 2)
            return -1.0f;

        float ratio = Clamp(accelDevNormOverAccelNormUseRatio, 0.0f, 1.0f);
        float accelNormAverageStartingFromZero = energyMeans[0] - 1.0f;
        if (accelNormAverageStartingFromZero < 0.0f)
            accelNormAverageStartingFromZero = 0.0f;

        return ((1.0f - ratio) * accelNormAverageStartingFromZero) + (ratio * energyMeans[1]);
    }

    private static float ComputeEnergyFactor(IReadOnlyList<float> energyMeans, IReadOnlyList<float> classifierEnergyMeans, float accelDevNormOverAccelNormUseRatio)
    {
        if (energyMeans.Count < 2 || classifierEnergyMeans.Count < 2)
            return -1.0f;

        float ratio = Clamp(accelDevNormOverAccelNormUseRatio, 0.0f, 1.0f);
        return ((1.0f - ratio) * (energyMeans[0] / classifierEnergyMeans[0]))
            + (ratio * (energyMeans[1] / classifierEnergyMeans[1]));
    }

    private static float ComputeAutoCorrelationValidationTime(
        IReadOnlyList<MotionAutoCorrelationSample> samples,
        float threshold,
        uint customizationBitField,
        MoveScoringOptions options)
    {
        if ((customizationBitField & IgnoreAutoCorrelationFlag) != 0 || threshold == -1.0f)
            return -6.0f;

        if (samples.Count < 2)
            return -7.0f;

        List<MotionAutoCorrelationSample> centered = CenterAutoCorrelationSignal(samples);
        float nonShiftedIntegral = ComputeAutoCorrelationNormalizedIntegral(centered, 0.0f);
        if (nonShiftedIntegral == -1.0f)
            return -7.0f;

        float minAutoCorrelationRatio = 1.0e+32f;
        float permissiveMaxTimeShift = options.AutoCorrelationMaxTimeShift + 0.001f;

        for (float timeShift = options.AutoCorrelationStepTimeShift; timeShift < permissiveMaxTimeShift; timeShift += options.AutoCorrelationStepTimeShift)
        {
            float shiftedIntegral = ComputeAutoCorrelationNormalizedIntegral(centered, timeShift);
            if (shiftedIntegral == -1.0f)
                return -8.0f;

            float autoCorrelationRatio = shiftedIntegral / nonShiftedIntegral;
            if (autoCorrelationRatio < minAutoCorrelationRatio)
                minAutoCorrelationRatio = autoCorrelationRatio;

            if (minAutoCorrelationRatio < 0.0f && autoCorrelationRatio > threshold)
                return timeShift;
        }

        return -9.0f;
    }

    private static List<MotionAutoCorrelationSample> CenterAutoCorrelationSignal(IReadOnlyList<MotionAutoCorrelationSample> samples)
    {
        float sum = 0.0f;
        for (int i = 0; i < samples.Count; i++)
            sum += samples[i].AccelNorm;

        float offset = sum / samples.Count;
        List<MotionAutoCorrelationSample> centered = new(samples.Count);
        for (int i = 0; i < samples.Count; i++)
            centered.Add(samples[i] with { AccelNorm = samples[i].AccelNorm - offset });
        return centered;
    }

    private static float ComputeAutoCorrelationNormalizedIntegral(IReadOnlyList<MotionAutoCorrelationSample> samples, float timeShift)
    {
        if (samples.Count < 2)
            return -1.0f;

        int shiftedIndex = 0;
        if (timeShift > 0.0f)
        {
            int lastSearchIndex = samples.Count - 1;
            while (shiftedIndex != lastSearchIndex)
            {
                if (samples[shiftedIndex].Time > timeShift)
                    break;

                shiftedIndex++;
            }

            if (shiftedIndex == lastSearchIndex)
                return -1.0f;
        }

        int baseIndex = 0;
        float previousProduct = samples[baseIndex].AccelNorm * samples[shiftedIndex].AccelNorm;
        float previousTime = 0.5f * (samples[baseIndex].Time + samples[shiftedIndex].Time);

        baseIndex++;
        shiftedIndex++;

        float integral = 0.0f;
        float totalTime = 0.0f;

        while (shiftedIndex != samples.Count)
        {
            float currentProduct = samples[baseIndex].AccelNorm * samples[shiftedIndex].AccelNorm;
            float currentTime = 0.5f * (samples[baseIndex].Time + samples[shiftedIndex].Time);
            float deltaTime = currentTime - previousTime;

            integral += 0.5f * (previousProduct + currentProduct) * deltaTime;
            totalTime += deltaTime;

            previousProduct = currentProduct;
            previousTime = currentTime;
            baseIndex++;
            shiftedIndex++;
        }

        return integral / totalTime;
    }

    private static (bool Ignored, float Impact) ComputeDirectionTendencyImpact(
        MotionClassifier classifier,
        MotionAnalysisResult analysis,
        MoveRuntimeSettings runtime)
    {
        if ((runtime.CustomizationBitField & IgnorePartsDirectionFlag) != 0)
            return (true, 0.0f);

        int partsCount = CountParts(analysis.Measures);
        if (partsCount == 0)
            return (true, 0.0f);

        PartAccelAverage[] partAverages = new PartAccelAverage[partsCount];
        int directionMeasuresCount = 0;
        for (int i = 0; i < analysis.Measures.Count; i++)
        {
            MotionMeasureResult measure = analysis.Measures[i];
            if (!IsDirectionMeasure(measure.MeasureId))
                continue;

            directionMeasuresCount++;
            ref PartAccelAverage part = ref partAverages[measure.PartPosition - 1];
            switch (measure.MeasureId)
            {
                case AxDevAvgDirNp:
                    part.ResultX = measure.Value;
                    part.MeanX = classifier.Means[i];
                    part.InvCovX = classifier.InvertedCovariances[i];
                    break;
                case AyDevAvgDirNp:
                    part.ResultY = measure.Value;
                    part.MeanY = classifier.Means[i];
                    part.InvCovY = classifier.InvertedCovariances[i];
                    break;
                case AzDevAvgDirNp:
                    part.ResultZ = measure.Value;
                    part.MeanZ = classifier.Means[i];
                    part.InvCovZ = classifier.InvertedCovariances[i];
                    break;
            }
        }

        if (directionMeasuresCount != 3 * partsCount)
            return (true, 0.0f);

        int directionTendency = 0;
        for (int i = 0; i < partAverages.Length; i++)
        {
            PartAccelAverage part = partAverages[i];
            float performedDistance = MathF.Sqrt(ComputeDirectionSqrDistance(part, invertResult: false));
            float invertedDistance = MathF.Sqrt(ComputeDirectionSqrDistance(part, invertResult: true));
            float tendency = invertedDistance - performedDistance;

            if (tendency > 0.0f)
                directionTendency++;
            else if (tendency < 0.0f)
                directionTendency--;
        }

        return (false, (float)directionTendency / partsCount * runtime.DirectionImpactFactor);
    }

    private static int CountParts(IReadOnlyList<MotionMeasureResult> measures)
    {
        int parts = 0;
        for (int i = 0; i < measures.Count; i++)
            parts = Math.Max(parts, measures[i].PartPosition);
        return parts;
    }

    private static float ComputeDirectionSqrDistance(PartAccelAverage part, bool invertResult)
    {
        float resultX = invertResult ? -part.ResultX : part.ResultX;
        float resultY = invertResult ? -part.ResultY : part.ResultY;
        float resultZ = invertResult ? -part.ResultZ : part.ResultZ;

        return (((resultX - part.MeanX) * (resultX - part.MeanX) * part.InvCovX)
            + ((resultY - part.MeanY) * (resultY - part.MeanY) * part.InvCovY)
            + ((resultZ - part.MeanZ) * (resultZ - part.MeanZ) * part.InvCovZ)) / 3.0f;
    }

    private static bool IsDirectionMeasure(byte measureId)
    {
        return measureId is AxDevAvgDirNp or AyDevAvgDirNp or AzDevAvgDirNp;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (float.IsNaN(value))
            return min;
        if (float.IsPositiveInfinity(value))
            return max;
        if (float.IsNegativeInfinity(value))
            return min;

        if (value < min)
            return min;
        return value > max ? max : value;
    }

    private static float SanitizeFinite(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    private readonly record struct MoveRuntimeSettings(
        float LowThreshold,
        float HighThreshold,
        float AutoCorrelationThreshold,
        float DirectionImpactFactor,
        uint CustomizationBitField);

    private struct PartAccelAverage
    {
        public float ResultX;
        public float ResultY;
        public float ResultZ;
        public float MeanX;
        public float MeanY;
        public float MeanZ;
        public float InvCovX;
        public float InvCovY;
        public float InvCovZ;
    }
}
