namespace JustDanceEditor.Formats.UbiArt.Services.Export;

public interface IUbiArtExporterFactory
{
    IPlatformExporter GetPlatformExporter(UbiArtPlatform platform);
    IEngineContentGenerator GetEngineContentGenerator(UbiArtEngineVersion version);
}