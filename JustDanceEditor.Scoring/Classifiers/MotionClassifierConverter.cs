using System.Buffers.Binary;

namespace JustDanceEditor.Scoring;

public static class MotionClassifierConverter
{
    public const MotionClassifierFormatVersion Version4 = MotionClassifierFormatVersion.Version4;
    public const MotionClassifierFormatVersion Version5 = MotionClassifierFormatVersion.Version5;
    public const MotionClassifierFormatVersion Version6 = MotionClassifierFormatVersion.Version6;
    public const MotionClassifierFormatVersion Version7 = MotionClassifierFormatVersion.Version7;
    public const MotionClassifierFormatVersion Version8 = MotionClassifierFormatVersion.Version8;
    public const float DynamicTenPartDuration = 0.83f;

    private const int TargetPartsCount = 10;
    private const int StandardSplitMeasuresCount = 5;
    private const ulong AccDevDirNpBitfield =
        (1UL << 50) |
        (1UL << 51) |
        (1UL << 52) |
        (1UL << 56) |
        (1UL << 61);
    private const double VarianceFloor = 1.0e-6;

    public static MotionClassifierFormatVersion GetFormatVersion(ReadOnlySpan<byte> source)
    {
        if (source.Length < 8)
            throw new InvalidDataException("Motion classifier data is too short.");

        if (BinaryPrimitives.ReadUInt32LittleEndian(source[..4]) == 1U)
            return MotionClassifierFormatRules.Parse(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)));
        if (BinaryPrimitives.ReadUInt32BigEndian(source[..4]) == 1U)
            return MotionClassifierFormatRules.Parse(BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4, 4)));

        throw new NotSupportedException("The motion classifier endianness marker is not recognized.");
    }

    public static byte[] ConvertToVersion(
        ReadOnlySpan<byte> source,
        MotionClassifierFormatVersion targetVersion,
        float? moveDuration = null)
    {
        if (!MotionClassifierFormatRules.IsNativelySupported(targetVersion))
            throw new NotSupportedException("MSM conversion targets versions 4 through 7.");

        return targetVersion switch
        {
            Version4 => ConvertToVersion4(source, moveDuration),
            Version5 => ConvertToVersion5(source, moveDuration),
            Version6 => ConvertToVersion6(source),
            Version7 => ConvertToVersion7(source),
            _ => throw new InvalidOperationException("The requested MSM conversion target is invalid.")
        };
    }

    public static byte[] ConvertToVersion4(ReadOnlySpan<byte> source, float? moveDuration = null)
        => ConvertToFixedTenPartVersion(source, Version4, moveDuration);

    public static byte[] ConvertToVersion7(ReadOnlySpan<byte> source)
    {
        MotionClassifier classifier = MotionClassifierReader.Read(source);
        EnsureNoSubClassifiers(classifier);

        if (classifier.FormatVersion == Version7)
            return source.ToArray();

        if (classifier.FormatVersion is not (Version4 or Version5 or Version6 or Version8))
            throw new NotSupportedException($"MSM version {(uint)classifier.FormatVersion} cannot be converted to version 7.");

        bool fixedTenParts = MotionClassifierFormatRules.UsesFixedTenParts(classifier.FormatVersion);
        string measureSetName = fixedTenParts
            ? GetNpMeasureSetName(classifier.MeasureSetName)
            : classifier.MeasureSetName;
        float duration = fixedTenParts
            ? DynamicTenPartDuration
            : classifier.Duration;

        return MotionClassifierWriter.Write(classifier with
        {
            FormatVersion = Version7,
            MeasureSetName = measureSetName,
            Duration = duration,
            AutoCorrelationThreshold = MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(classifier.FormatVersion) ? classifier.AutoCorrelationThreshold : -1.0f,
            DirectionImpactFactor = MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(classifier.FormatVersion) ? classifier.DirectionImpactFactor : -1.0f,
            SubClassifiersCount = 0
        });
    }

    public static byte[] ConvertToVersion6(ReadOnlySpan<byte> source)
    {
        MotionClassifier classifier = MotionClassifierReader.Read(source);
        EnsureNoSubClassifiers(classifier);

        if (classifier.FormatVersion == Version6)
            return source.ToArray();

        bool fixedTenParts = MotionClassifierFormatRules.UsesFixedTenParts(classifier.FormatVersion);
        return MotionClassifierWriter.Write(classifier with
        {
            FormatVersion = Version6,
            MeasureSetName = fixedTenParts ? GetNpMeasureSetName(classifier.MeasureSetName) : classifier.MeasureSetName,
            Duration = fixedTenParts ? DynamicTenPartDuration : classifier.Duration,
            AutoCorrelationThreshold = -1.0f,
            DirectionImpactFactor = -1.0f,
            SubClassifiersCount = 0
        });
    }

    public static byte[] ConvertToVersion5(ReadOnlySpan<byte> source, float? moveDuration = null)
        => ConvertToFixedTenPartVersion(source, Version5, moveDuration);

    private static byte[] ConvertToFixedTenPartVersion(
        ReadOnlySpan<byte> source,
        MotionClassifierFormatVersion targetVersion,
        float? moveDuration)
    {
        MotionClassifier classifier = MotionClassifierReader.Read(source);
        EnsureNoSubClassifiers(classifier);

        if (classifier.FormatVersion == targetVersion && !moveDuration.HasValue)
            return source.ToArray();

        if (classifier.ScoringAlgorithmType <= 0)
            throw new NotSupportedException($"Version {(uint)targetVersion} conversion currently requires a Naive Bayes MSM.");
        if (classifier.MeasureSetBitfield != AccDevDirNpBitfield)
            throw new NotSupportedException($"Version {(uint)targetVersion} conversion currently requires the Acc_Dev_Dir_NP measure set.");
        if (classifier.Means.Length % StandardSplitMeasuresCount != 0)
            throw new InvalidDataException("The MSM measure count is not divisible by the five Acc_Dev_Dir_NP measures.");
        if (classifier.InvertedCovariances.Length != classifier.Means.Length)
            throw new InvalidDataException("The Naive Bayes MSM does not contain one inverted variance per mean.");

        int sourcePartsCount = classifier.Means.Length / StandardSplitMeasuresCount;
        if (sourcePartsCount <= 0)
            throw new InvalidDataException("The MSM does not contain any move-analysis parts.");

        float[] means = sourcePartsCount == TargetPartsCount
            ? [.. classifier.Means]
            : ResampleMeans(classifier.Means, sourcePartsCount);
        float[] invertedVariances = sourcePartsCount == TargetPartsCount
            ? [.. classifier.InvertedCovariances]
            : ResampleInvertedVariances(classifier.InvertedCovariances, sourcePartsCount);

        float duration = moveDuration is float requestedDuration && requestedDuration > 0.0f && float.IsFinite(requestedDuration)
            ? requestedDuration
            : classifier.Duration;

        return MotionClassifierWriter.Write(classifier with
        {
            FormatVersion = targetVersion,
            MeasureSetName = GetTenPartMeasureSetName(classifier.MeasureSetName),
            Duration = duration,
            AutoCorrelationThreshold = -1.0f,
            DirectionImpactFactor = -1.0f,
            CustomizationBitField = targetVersion == Version4 ? 0U : classifier.CustomizationBitField,
            ScoringAlgorithmType = means.Length,
            Means = means,
            InvertedCovariances = invertedVariances,
            SubClassifiersCount = 0
        });
    }

    private static float[] ResampleMeans(IReadOnlyList<float> source, int sourcePartsCount)
    {
        float[] result = new float[StandardSplitMeasuresCount * TargetPartsCount];
        for (int measure = 0; measure < StandardSplitMeasuresCount; measure++)
        {
            int sourceOffset = measure * sourcePartsCount;
            int targetOffset = measure * TargetPartsCount;
            for (int targetPart = 0; targetPart < TargetPartsCount; targetPart++)
            {
                double value = 0.0;
                foreach ((int sourcePart, double weight) in GetOverlaps(sourcePartsCount, targetPart))
                    value += weight * Finite(source[sourceOffset + sourcePart]);
                result[targetOffset + targetPart] = (float)value;
            }
        }
        return result;
    }

    private static float[] ResampleInvertedVariances(IReadOnlyList<float> source, int sourcePartsCount)
    {
        float[] result = new float[StandardSplitMeasuresCount * TargetPartsCount];
        for (int measure = 0; measure < StandardSplitMeasuresCount; measure++)
        {
            int sourceOffset = measure * sourcePartsCount;
            int targetOffset = measure * TargetPartsCount;
            for (int targetPart = 0; targetPart < TargetPartsCount; targetPart++)
            {
                double variance = 0.0;
                foreach ((int sourcePart, double weight) in GetOverlaps(sourcePartsCount, targetPart))
                {
                    double invertedVariance = Finite(source[sourceOffset + sourcePart]);
                    double sourceVariance = invertedVariance > 0.0 ? 1.0 / invertedVariance : VarianceFloor;
                    variance += weight * weight * sourceVariance;
                }
                result[targetOffset + targetPart] = (float)(1.0 / Math.Max(variance, VarianceFloor));
            }
        }
        return result;
    }

    private static IEnumerable<(int SourcePart, double Weight)> GetOverlaps(int sourcePartsCount, int targetPart)
    {
        double targetStart = targetPart / (double)TargetPartsCount;
        double targetEnd = (targetPart + 1) / (double)TargetPartsCount;
        double targetWidth = targetEnd - targetStart;

        int firstSource = Math.Max(0, (int)Math.Floor(targetStart * sourcePartsCount));
        int lastSource = Math.Min(sourcePartsCount - 1, (int)Math.Ceiling(targetEnd * sourcePartsCount) - 1);
        for (int sourcePart = firstSource; sourcePart <= lastSource; sourcePart++)
        {
            double sourceStart = sourcePart / (double)sourcePartsCount;
            double sourceEnd = (sourcePart + 1) / (double)sourcePartsCount;
            double overlap = Math.Max(0.0, Math.Min(targetEnd, sourceEnd) - Math.Max(targetStart, sourceStart));
            if (overlap > 0.0)
                yield return (sourcePart, overlap / targetWidth);
        }
    }

    private static string GetNpMeasureSetName(string name)
        => name.EndsWith("_10P", StringComparison.OrdinalIgnoreCase)
            ? name[..^4] + "_NP"
            : name;

    private static string GetTenPartMeasureSetName(string name)
        => name.EndsWith("_NP", StringComparison.OrdinalIgnoreCase)
            ? name[..^3] + "_10P"
            : name;

    private static double Finite(float value)
        => float.IsFinite(value) ? value : 0.0;

    private static void EnsureNoSubClassifiers(MotionClassifier classifier)
    {
        if (classifier.SubClassifiersCount != 0)
            throw new NotSupportedException("MSMs with sub-classifiers cannot be converted without losing data.");
    }
}
