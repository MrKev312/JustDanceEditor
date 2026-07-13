namespace JustDanceEditor.Scoring;

/// <summary>
/// MSM classifier format versions understood by the scoring library.
/// Versions 4 through 7 support native generation and scoring. Version 8 is
/// exposed for header inspection and safe conversion when no subclassifiers exist.
/// </summary>
public enum MotionClassifierFormatVersion : uint
{
    Version4 = 4,
    Version5 = 5,
    Version6 = 6,
    Version7 = 7,
    Version8 = 8
}

public static class MotionClassifierFormats
{
    public static IReadOnlyList<MotionClassifierFormatVersion> NativeVersions { get; } = Array.AsReadOnly(
    [
        MotionClassifierFormatVersion.Version4,
        MotionClassifierFormatVersion.Version5,
        MotionClassifierFormatVersion.Version6,
        MotionClassifierFormatVersion.Version7
    ]);
}

internal static class MotionClassifierFormatRules
{
    public static bool IsNativelySupported(MotionClassifierFormatVersion version)
        => version is >= MotionClassifierFormatVersion.Version4 and <= MotionClassifierFormatVersion.Version7;

    public static bool UsesFixedTenParts(MotionClassifierFormatVersion version)
        => version is MotionClassifierFormatVersion.Version4 or MotionClassifierFormatVersion.Version5;

    public static bool HasCustomizationBitField(MotionClassifierFormatVersion version)
        => version >= MotionClassifierFormatVersion.Version5;

    public static bool HasAutoCorrelationAndDirectionFields(MotionClassifierFormatVersion version)
        => version >= MotionClassifierFormatVersion.Version7;

    public static MotionClassifierFormatVersion Parse(uint version)
        => version switch
        {
            4 => MotionClassifierFormatVersion.Version4,
            5 => MotionClassifierFormatVersion.Version5,
            6 => MotionClassifierFormatVersion.Version6,
            7 => MotionClassifierFormatVersion.Version7,
            8 => MotionClassifierFormatVersion.Version8,
            _ => throw new NotSupportedException($"MSM version {version} is not supported.")
        };
}