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
        // JD2017 uses slightly less padding in menuart actors
        if (version == UbiArtEngineVersion.JD2017)
        {
            return new JD2017EngineContentGenerator(version);
        }

        // 2018-2022 share the same schema
        if (version is >= UbiArtEngineVersion.JD2018 and <= UbiArtEngineVersion.JD2022)
        {
            return new ModernEngineContentGenerator(version);
        }

        throw new NotImplementedException($"Engine {version} is not yet supported.");
    }
}