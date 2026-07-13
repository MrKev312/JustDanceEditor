namespace JustDanceEditor.Scoring;

public sealed class MotionClassifierGenerator
{
    public const string MeasureSetName = "Acc_Dev_Dir_NP";
    public const string TenPartMeasureSetName = "Acc_Dev_Dir_10P";
    private const double VarianceFloor = 1.0e-6;

    private const ulong AccDevDirNpBitfield =
        (1UL << 50) |
        (1UL << 51) |
        (1UL << 52) |
        (1UL << 56) |
        (1UL << 61);

    public byte[] BuildClassifier(MotionClassifierBuildRequest request)
    {
        if (request.Examples.Count == 0)
            throw new InvalidOperationException($"No examples were supplied for {request.MoveName}.");

        MotionClassifierGenerationOptions settings = request.Options;
        if (!MotionClassifierFormatRules.IsNativelySupported(settings.ClassifierFormatVersion))
            throw new NotSupportedException("Motion classifier generation supports MSM versions 4 through 7.");

        float classifierDuration = request.Examples.Min(static example => example.Duration);
        MoveSignalAnalyzer analyzer = new(settings.SmoothingFrequency, settings.ClassifierFormatVersion);
        List<MotionObservation> observations = [];

        foreach (MotionExample example in request.Examples)
        {
            if (example.Samples.Count == 0)
                continue;

            (List<float> measures, List<float> energyMeasures) = analyzer.Analyze(
                example.Samples,
                classifierMoveDuration: classifierDuration,
                gameMoveDuration: example.Duration,
                accelSaturationValue: settings.AccelSaturationValue);

            observations.Add(new MotionObservation(measures, energyMeasures));
        }

        string measureSetName = MotionClassifierFormatRules.UsesFixedTenParts(settings.ClassifierFormatVersion)
            ? TenPartMeasureSetName
            : MeasureSetName;
        MotionModelData model = new(request.SongName, request.MoveName, measureSetName, classifierDuration, observations);
        MotionClassifierData classifier = ComputeClassifier(model);
        return MotionClassifierWriter.Write(classifier, AccDevDirNpBitfield, settings);
    }

    public IReadOnlyDictionary<string, byte[]> BuildClassifiers(IEnumerable<MotionClassifierBuildRequest> requests)
    {
        return requests.ToDictionary(
            static request => request.MoveName,
            BuildClassifier,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<MotionClassifierFormatVersion, byte[]> BuildAllVersions(MotionClassifierBuildRequest request)
    {
        Dictionary<MotionClassifierFormatVersion, byte[]> result = [];
        foreach (MotionClassifierFormatVersion version in MotionClassifierFormats.NativeVersions)
        {
            result.Add(version, BuildClassifier(request with
            {
                Options = request.Options with { ClassifierFormatVersion = version }
            }));
        }

        return result;
    }

    private static MotionClassifierData ComputeClassifier(MotionModelData model)
    {
        List<double[]> measureVectors = [.. model.Observations
            .Where(static observation => observation.Measures.Count > 0)
            .Select(static observation => observation.Measures.Select(static value => (double)value).ToArray())];
        if (measureVectors.Count == 0)
            throw new InvalidOperationException($"No measure vectors were produced for {model.ModelName}.");

        double[] means = VectorMean(measureVectors);
        double[] variances = VectorVariance(measureVectors, means);

        List<double[]> energyVectors = [.. model.Observations
            .Where(static observation => observation.EnergyMeasures.Count > 0)
            .Select(static observation => observation.EnergyMeasures.Select(static value => (double)value).ToArray())];
        double[] energyMeans = energyVectors.Count == 0 ? [] : VectorMean(energyVectors);

        return new MotionClassifierData(model.SongName, model.ModelName, model.MeasureSetName, model.Duration, means, variances, energyMeans);
    }

    private static double[] VectorMean(IReadOnlyList<double[]> vectors)
    {
        int size = vectors[0].Length;
        double[] result = new double[size];

        for (int i = 0; i < size; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < vectors.Count; j++)
                sum += Sanitize(vectors[j][i]);
            result[i] = sum / vectors.Count;
        }

        return result;
    }

    private static double[] VectorVariance(IReadOnlyList<double[]> vectors, IReadOnlyList<double> means)
    {
        int size = means.Count;
        double[] result = new double[size];

        for (int i = 0; i < size; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < vectors.Count; j++)
            {
                double value = Sanitize(vectors[j][i]);
                double mean = Sanitize(means[i]);
                sum += Math.Pow(value - mean, 2.0);
            }

            result[i] = Math.Max(sum / vectors.Count, VarianceFloor);
        }

        return result;
    }

    private static double Sanitize(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
}