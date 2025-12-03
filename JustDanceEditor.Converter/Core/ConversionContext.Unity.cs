using JustDanceEditor.Formats.Unity;

namespace JustDanceEditor.Converter.Core;

public partial class ConversionContext
{
    public UnityExportData? UnityData { get; set; }

    public UnityExportData RequireUnityData() =>
        UnityData ?? throw new InvalidOperationException("Unity export data has not been initialized for this conversion context.");

    private partial string? TryGetSongNameFromUnity()
        => string.IsNullOrWhiteSpace(UnityData?.Name) ? null : UnityData.Name;

    private partial int? TryGetCoachCountFromUnity()
        => UnityData?.Metadata.CoachCount;
}
