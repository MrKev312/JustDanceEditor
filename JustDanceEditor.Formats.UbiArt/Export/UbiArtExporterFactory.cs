using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import;

namespace JustDanceEditor.Formats.UbiArt.Export;

public class UbiArtExporterFactory : IUbiArtExporterFactory
{
    public IPlatformExporter GetPlatformExporter(UbiArtPlatform platform)
    {
        return platform switch
        {
            UbiArtPlatform.NX => new NxCookedPlatformExporter(),
            UbiArtPlatform.PC => new PcCookedPlatformExporter(),
            UbiArtPlatform.Wii => new WiiCookedPlatformExporter(),
            UbiArtPlatform.WiiU => new WiiUCookedPlatformExporter(),
            UbiArtPlatform.Uncooked => new UncookedPlatformExporter(),
            _ => throw new NotImplementedException($"Platform {platform} is not yet supported.")
        };
    }

    public IEngineContentGenerator GetEngineContentGenerator(UbiArtEngineVersion version, UbiArtPlatform platform)
    {
        // Uncooked platform always uses Lua format, regardless of engine version
        if (platform == UbiArtPlatform.Uncooked)
        {
            return new UncookedEngineContentGenerator(version);
        }

        // Special case: Wii 2016-2020 uses legacy binary format
        if (platform == UbiArtPlatform.Wii && version is >= UbiArtEngineVersion.JD2016 and <= UbiArtEngineVersion.JD2020)
        {
            return new LegacyEngineContentGenerator(version);
        }

        // JD2017 uses slightly less padding in menuart actors
        if (version == UbiArtEngineVersion.JD2017)
        {
            return new JD2017EngineContentGenerator(version);
        }

        // 2018-2022 share the same schema (Modern)
        if (version is >= UbiArtEngineVersion.JD2018 and <= UbiArtEngineVersion.JD2022)
        {
            return new ModernEngineContentGenerator(version);
        }

        // Fallback for unknown newer/older, assume modern if high, 2015 if low?
        // Default to Modern for safety.
        return new ModernEngineContentGenerator(version);
    }
}