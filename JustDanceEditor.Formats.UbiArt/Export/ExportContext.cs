using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

namespace JustDanceEditor.Formats.UbiArt.Export;

/// <summary>
/// Context object to pass environment configuration to Exporters.
/// </summary>
public record ExportContext(
    string OutputFolder,
    IUbiArtLayout Layout,
    IFileSystem IO,
    UbiArtEngineVersion EngineVersion = UbiArtEngineVersion.Unknown,
    IMediaProcessor? MediaProcessor = null
)
{
    public IMediaProcessor GetMediaProcessor() => MediaProcessor ?? new DefaultMediaProcessor(IO);
}