using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Export;

internal sealed record UbiArtExportPlan(
    IntermediateSongPackage Package,
    string? MaterializedRoot,
    UbiArtPlatform Platform,
    UbiArtEngineVersion EngineVersion,
    IUbiArtLayout Layout,
    IPlatformExporter PlatformExporter,
    IEngineContentGenerator EngineGenerator,
    ExportContext Context,
    string MapWorldBase,
    string RawMapWorldBase)
{
    public string MapName => Package.Metadata.MapName;
    public string MapNameLower => MapName.ToLowerInvariant();
    public string PlatformRoot => PlatformExporter.GetPlatformRootFolder(MapNameLower);
}