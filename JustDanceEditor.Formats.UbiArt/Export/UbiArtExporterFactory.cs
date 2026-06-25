using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Export;

public class UbiArtExporterFactory : IUbiArtExporterFactory
{
    public IPlatformExporter GetPlatformExporter(UbiArtPlatform platform)
    {
        return platform switch
        {
            UbiArtPlatform.Uncooked => new UncookedPlatformExporter(),
            UbiArtPlatform.Revolution => new WiiCookedPlatformExporter(),
            UbiArtPlatform.Cafe => new WiiUCookedPlatformExporter(),
            UbiArtPlatform.NX => new NxCookedPlatformExporter(),
            UbiArtPlatform.Win32 => new PcCookedPlatformExporter(),
            UbiArtPlatform.Cell => new Ps3CookedPlatformExporter(),
            UbiArtPlatform.Xenon => new X360CookedPlatformExporter(),
            UbiArtPlatform.Durango => new DurangoCookedPlatformExporter(),
            UbiArtPlatform.Orbis => new OrbisCookedPlatformExporter(),
            _ => throw new NotSupportedException($"Platform {platform} is not supported.")
        };
    }

    public IEngineContentGenerator GetEngineContentGenerator(UbiArtEngineVersion version, UbiArtPlatform platform)
    {
        // Uncooked platform always uses Lua format, regardless of engine version
        if (platform == UbiArtPlatform.Uncooked)
        {
            return new UncookedEngineContentGenerator();
        }

        // JD2014/JD2015 cooked builds use binary engine resources on every cooked platform.
        if (platform != UbiArtPlatform.Uncooked && version is UbiArtEngineVersion.JD2014 or UbiArtEngineVersion.JD2015)
        {
            return new LegacyEngineContentGenerator(version, platform);
        }

        // Wii, PS3 and Xbox 360 kept using legacy binary engine resources through the later old-engine titles.
        if (platform is UbiArtPlatform.Revolution or UbiArtPlatform.Cell or UbiArtPlatform.Xenon && version is >= UbiArtEngineVersion.JD2016 and <= UbiArtEngineVersion.JD2020)
        {
            return new LegacyEngineContentGenerator(version, platform);
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