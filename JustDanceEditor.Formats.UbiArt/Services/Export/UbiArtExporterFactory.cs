using JustDanceEditor.Formats.UbiArt.Services.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Services.Export.Platform;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

public class UbiArtExporterFactory : IUbiArtExporterFactory
{
    public IPlatformExporter GetPlatformExporter(UbiArtPlatform platform)
    {
        return platform switch
        {
            UbiArtPlatform.NX => new NxCookedPlatformExporter(),
            UbiArtPlatform.Uncooked => new UncookedPlatformExporter(),
            _ => throw new NotImplementedException($"Platform {platform} is not yet supported.")
        };
    }

    public IEngineContentGenerator GetEngineContentGenerator(UbiArtEngineVersion version)
    {
        // 2019-2022 share the same schema
        if (version is >= UbiArtEngineVersion.JD2019 and <= UbiArtEngineVersion.JD2022)
        {
            return new ModernEngineContentGenerator(version);
        }

        throw new NotImplementedException($"Engine {version} is not yet supported.");
    }
}