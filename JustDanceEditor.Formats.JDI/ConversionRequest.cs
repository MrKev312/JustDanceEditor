namespace JustDanceEditor.Formats.JDI;

public enum ExportType
{
    OfflineCache,
    CustomServer
}

public abstract class ConversionRequestBase
{
    protected ConversionRequestBase(string inputPath, string outputPath)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
    }

    public string InputPath { get; set; }
    public string OutputPath { get; set; }
}

// Used for UbiArt -> JDI
public class UbiArtConversionRequest(string inputPath, string outputPath, string? songName)
    : ConversionRequestBase(inputPath, outputPath)
{
    // SongName is required to disambiguate if the input folder contains multiple maps
    public string? SongName { get; set; } = songName;
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