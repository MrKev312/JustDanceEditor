using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

/// <summary>
/// Context object to pass environment configuration to Exporters.
/// </summary>
public record ExportContext(
    string OutputFolder,
    IUbiArtLayout Layout,
    IFileSystem IO
);