using JustDanceEditor.Formats.UbiArt.Import;
namespace JustDanceEditor.Formats.UbiArt.Export;

public interface IUbiArtExporterFactory
{
    IPlatformExporter GetPlatformExporter(UbiArtPlatform platform);
    IEngineContentGenerator GetEngineContentGenerator(UbiArtEngineVersion version, UbiArtPlatform platform);
}