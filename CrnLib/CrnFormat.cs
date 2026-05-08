namespace CrnLib;

public enum CrnFormat
{
    Dxt1 = 0,
    Dxt5 = 2,
}

public enum CrnCompressionQuality
{
    Fast,
    Balanced,
    BestQuality,
}

public sealed class CrnEncodeOptions
{
    public int MipCount { get; init; } = 1;
    public CrnCompressionQuality Quality { get; init; } = CrnCompressionQuality.BestQuality;
    public uint UserData0 { get; init; } = 1;
    public uint UserData1 { get; init; }
}

public readonly record struct CrnTextureInfo(
    int Width,
    int Height,
    int Levels,
    int Faces,
    CrnFormat Format,
    int BytesPerBlock,
    uint UserData0,
    uint UserData1);
