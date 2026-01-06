namespace JustDanceEditor.Formats.JDI.Services;

public interface IAudioConverter
{
    // Convert audio from a source stream (sourceFileName is the original filename, used for format detection).
    // tempFolder, if provided, may be used to write temporary input files (e.g., for external converters that require actual files).
    Task Convert(Stream source, string sourceFileName, string targetPath, string? tempFolder = null);
}