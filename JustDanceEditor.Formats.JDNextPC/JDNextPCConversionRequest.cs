using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.JDNextPC;

/// <summary>
/// Conversion request for JDNext PC format operations.
/// </summary>
public sealed class JDNextPCConversionRequest(string inputPath, string outputPath)
    : ConversionRequestBase(inputPath, outputPath);