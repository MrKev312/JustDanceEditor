namespace CrnLib;

public enum CrnFormat
{
    Dxt1 = 0,
    Dxt3 = 1,
    Dxt5 = 2,
    Dxt5CcxY = 3,
    Dxt5XGxR = 4,
    Dxt5XGbr = 5,
    Dxt5Agbr = 6,
    DxnXy = 7,
    DxnYx = 8,
    Dxt5A = 9,
    Etc1 = 10,
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

public readonly record struct CrnFileInfo(
    int ActualDataSize,
    int HeaderSize,
    int TotalPaletteSize,
    int TablesSize,
    int Levels,
    int[] LevelCompressedSizes,
    int ColorEndpointPaletteEntries,
    int ColorSelectorPaletteEntries,
    int AlphaEndpointPaletteEntries,
    int AlphaSelectorPaletteEntries,
    bool IsSegmented);

public readonly record struct CrnLevelInfo(
    int Width,
    int Height,
    int Faces,
    int BlocksX,
    int BlocksY,
    int BytesPerBlock,
    CrnFormat Format);

public readonly record struct CrnLevelDataRange(int Offset, int Size);
