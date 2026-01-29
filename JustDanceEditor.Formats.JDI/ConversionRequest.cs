namespace JustDanceEditor.Formats.JDI;

public enum ExportType
{
    OfflineCache,
    CustomServer
}

public enum UbiArtType
{
    Cooked,
    Uncooked
}

// UbiArt Platform for export/import request - mirrored from Services for serialization independence
public enum UbiArtPlatformType
{
    Uncooked,
    Wii,
    WiiU,
    NX,
    PC
}

// UbiArt Engine Version for export/import request - mirrored from Services for serialization independence
public enum UbiArtEngineVersionType
{
    Unknown,
    JD2014 = 2014,
    JD2015,
    JD2016,
    JD2017,
    JD2018,
    JD2019,
    JD2020,
    JD2021,
    JD2022
}

public abstract class ConversionRequestBase(string inputPath, string outputPath)
{
    public string InputPath { get; set; } = inputPath;
    public string OutputPath { get; set; } = outputPath;
}

// Used for UbiArt -> JDI
public class UbiArtConversionRequest(string inputPath, string outputPath, string? songName)
    : ConversionRequestBase(inputPath, outputPath)
{
    // SongName is required to disambiguate if the input folder contains multiple maps
    public string? SongName { get; set; } = songName;
    public UbiArtType Type { get; set; } = UbiArtType.Cooked;

    // For IMPORT: which platform/engine are we importing FROM (will be auto-detected during import)
    public UbiArtPlatformType? ImportPlatform { get; set; }
    public UbiArtEngineVersionType? ImportEngineVersion { get; set; }

    // For EXPORT: which platform and engine should we export TO
    public UbiArtPlatformType ExportPlatform { get; set; } = UbiArtPlatformType.Uncooked;
    public UbiArtEngineVersionType ExportEngineVersion { get; set; } = UbiArtEngineVersionType.JD2022;

    // Optional delegate to allow callers (UI) to select a song when multiple are present.
    // Should return the chosen song name, or null/empty to cancel.
    public Func<string[], Task<string?>>? SelectSongAsync { get; set; }
}

// Used for JDI -> Unity
public class UnityConversionRequest(string inputPath, string outputPath, string templatePath)
    : ConversionRequestBase(inputPath, outputPath)
{
    public string TemplatePath { get; set; } = templatePath;
    public bool OnlineCover { get; set; }
    public ExportType ExportType { get; set; } = ExportType.CustomServer;
    // CacheNumber only relevant if ExportType == OfflineCache
    public uint? CacheNumber { get; set; }
}

// Used for JDI -> JDI (Format Updates/Passthrough)
public class JdiConversionRequest(string inputPath, string outputPath)
    : ConversionRequestBase(inputPath, outputPath);