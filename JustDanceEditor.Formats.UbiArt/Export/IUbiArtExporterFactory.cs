using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Export;

public interface IUbiArtExporterFactory
{
    IPlatformExporter GetPlatformExporter(UbiArtPlatform platform);
    IEngineContentGenerator GetEngineContentGenerator(UbiArtEngineVersion version, UbiArtPlatform platform);
}