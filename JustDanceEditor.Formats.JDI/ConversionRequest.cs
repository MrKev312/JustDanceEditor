namespace JustDanceEditor.Formats.JDI;

/// <summary>
/// Base class for all format-specific conversion requests.
/// Each format project defines its own derived request type.
/// </summary>
public abstract class ConversionRequestBase(string inputPath, string outputPath)
{
    /// <summary>
    /// The input path (folder or file) to convert from.
    /// </summary>
    public string InputPath { get; set; } = inputPath;

    /// <summary>
    /// The output path to write the converted result to.
    /// </summary>
    public string OutputPath { get; set; } = outputPath;
}

/// <summary>
/// Conversion request for JDI to JDI operations (format updates/passthrough).
/// </summary>
public class JdiConversionRequest(string inputPath, string outputPath)
    : ConversionRequestBase(inputPath, outputPath);