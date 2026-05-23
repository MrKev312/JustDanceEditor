namespace JustDanceEditor.Formats.JDI.Video;

public sealed record JdiVideoEncodeRequest(
    string PackageRoot,
    string CacheFileName,
    string ContainerExtension,
    string Codec)
{
    public string? SourcePath { get; init; }
    public JdiVideoTransform Transform { get; init; } = JdiVideoTransform.None;
    public JdiVideoEncodingSettings Encoding { get; init; } = new();
    public TimeSpan Start { get; init; }
    public TimeSpan Duration { get; init; }
    public bool ForceTranscode { get; init; }
}

public sealed record JdiVideoTransform
{
    public static JdiVideoTransform None { get; } = new();

    public int? Width { get; init; }
    public int? Height { get; init; }
    public JdiScaleAlgorithm ScaleAlgorithm { get; init; } = JdiScaleAlgorithm.Default;
    public string? SampleAspectRatio { get; init; }
    public string? DisplayAspectRatio { get; init; }
    public IReadOnlyList<string> AdditionalFilters { get; init; } = [];

    public bool IsEmpty =>
        Width is null &&
        Height is null &&
        string.IsNullOrWhiteSpace(SampleAspectRatio) &&
        string.IsNullOrWhiteSpace(DisplayAspectRatio) &&
        AdditionalFilters.Count == 0;
}

public enum JdiScaleAlgorithm
{
    Default,
    Bicubic
}

public sealed record JdiVideoEncodingSettings
{
    public bool UseDefaultCodecTuning { get; init; } = true;
    public long? Bitrate { get; init; }
    public long? MaxBitrate { get; init; }
    public long? BufferSize { get; init; }
    public int? ConstantRateFactor { get; init; }
    public string? PixelFormat { get; init; }
    public int? Profile { get; init; }
    public bool? AutoAltRef { get; init; }
    public string? Quality { get; init; }
    public int? CpuUsed { get; init; }
    public int? Speed { get; init; }
    public int? Slices { get; init; }
    public bool? RowMultithreading { get; init; }
    public bool OmitAudio { get; init; } = true;
}
