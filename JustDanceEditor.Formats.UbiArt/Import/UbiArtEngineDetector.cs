using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import;

public class UbiArtEngineDetector(IUbiArtFileSystem? io = null) : IUbiArtEngineDetector
{
    private readonly IUbiArtFileSystem _io = io ?? new PhysicalUbiArtFileSystem();

    public UbiArtVersionProfile Detect(string inputPath, string? mapName = null)
    {
        if (Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            using UbiArtIpkFileSystem ipk = new(inputPath);
            return DetectWithFileSystem(string.Empty, ipk, inputPath, mapName);
        }

        return DetectWithFileSystem(inputPath, _io, inputPath, mapName);
    }

    private static UbiArtVersionProfile DetectWithFileSystem(
        string basePath,
        IUbiArtFileSystem fs,
        string sourcePath,
        string? mapName)
    {
        return fs.DirectoryExists(fs.Combine(basePath, "cache", "itf_cooked"))
            ? DetectCooked(basePath, fs, sourcePath, mapName)
            : DetectUncooked(basePath, fs, sourcePath, mapName);
    }

    private static UbiArtVersionProfile DetectUncooked(
        string basePath,
        IUbiArtFileSystem fs,
        string sourcePath,
        string? mapName)
    {
        if (!TryDetectMapsFolder(basePath, fs, out string mapsFolder, out UbiArtEngineVersion rootVersion))
        {
            mapsFolder = string.Empty;
            rootVersion = UbiArtEngineVersion.JD2022;
        }

        using UbiArtSongDescriptorResolver descriptors = new(
            fs,
            basePath,
            cookedPlatformRoot: null,
            mapsFolder,
            sourcePath,
            UbiArtPlatform.Uncooked);
        string? resolvedMapName = descriptors.ResolveMapName(mapName);
        UbiArtEngineVersion engineVersion = ResolveEngineVersion(rootVersion, resolvedMapName, descriptors, out _);
        return CreateProfile(UbiArtPlatform.Uncooked, rootVersion, engineVersion);
    }

    private static UbiArtVersionProfile DetectCooked(
        string basePath,
        IUbiArtFileSystem fs,
        string sourcePath,
        string? mapName)
    {
        string cookedRoot = fs.Combine(basePath, "cache", "itf_cooked");
        string[] platformFolders = fs.GetDirectories(cookedRoot);
        if (platformFolders.Length == 0)
            throw new DirectoryNotFoundException("No platform folders found in the itf_cooked folder.");
        if (platformFolders.Length > 1)
            throw new DirectoryNotFoundException("Multiple platform folders found in the itf_cooked folder, this is not supported.");

        string platformFolderName = Path.GetFileName(platformFolders[0]);
        if (!UbiArtPlatformExtensions.TryParseCookedFolderName(platformFolderName, out UbiArtPlatform platform))
            throw new InvalidOperationException($"Unknown UbiArt platform folder '{platformFolderName}' in cache/itf_cooked.");
        if (platform == UbiArtPlatform.Uncooked)
            throw new InvalidOperationException("Invalid platform 'uncooked' inside cache/itf_cooked.");

        string platformRoot = fs.Combine(cookedRoot, platformFolderName);
        if (!TryDetectMapsFolder(platformRoot, fs, out string mapsFolder, out UbiArtEngineVersion rootVersion))
        {
            mapsFolder = Path.Combine("world", "maps");
            rootVersion = UbiArtEngineVersion.JD2022;
        }

        using UbiArtSongDescriptorResolver descriptors = new(
            fs,
            basePath,
            platformRoot,
            mapsFolder,
            sourcePath,
            platform);
        string? resolvedMapName = descriptors.ResolveMapName(mapName);
        UbiArtEngineVersion engineVersion = ResolveEngineVersion(rootVersion, resolvedMapName, descriptors, out bool descriptorIsUncooked);
        UbiArtPlatform profilePlatform = descriptorIsUncooked ? UbiArtPlatform.Uncooked : platform;
        return CreateProfile(profilePlatform, rootVersion, engineVersion);
    }

    private static UbiArtEngineVersion ResolveEngineVersion(
        UbiArtEngineVersion rootVersion,
        string? mapName,
        UbiArtSongDescriptorResolver descriptors,
        out bool descriptorIsUncooked)
    {
        descriptorIsUncooked = false;
        if (rootVersion != UbiArtEngineVersion.JD2022)
            return rootVersion;

        if (descriptors.TryReadModern(mapName, out string content, out descriptorIsUncooked))
        {
            return UbiArtSongDescriptorVersionReader.TryReadModern(content, out UbiArtEngineVersion modernVersion)
                ? modernVersion
                : rootVersion;
        }

        descriptorIsUncooked = false;
        if (descriptors.TryReadLegacy(mapName, out byte[] bytes) &&
            UbiArtSongDescriptorVersionReader.TryReadLegacy(bytes, out UbiArtEngineVersion legacyVersion))
        {
            return legacyVersion;
        }

        return rootVersion;
    }

    private static bool TryDetectMapsFolder(
        string contentRoot,
        IUbiArtFileSystem fs,
        out string mapsFolder,
        out UbiArtEngineVersion engineVersion)
    {
        (string FolderName, UbiArtEngineVersion EngineVersion)[] candidates =
        [
            ("maps", UbiArtEngineVersion.JD2022),
            ("jd2015", UbiArtEngineVersion.JD2015),
            ("jd5", UbiArtEngineVersion.JD2014)
        ];

        foreach ((string folderName, UbiArtEngineVersion candidateVersion) in candidates)
        {
            if (!fs.DirectoryExists(fs.Combine(contentRoot, "world", folderName)))
                continue;

            mapsFolder = Path.Combine("world", folderName);
            engineVersion = candidateVersion;
            return true;
        }

        mapsFolder = string.Empty;
        engineVersion = UbiArtEngineVersion.Unknown;
        return false;
    }

    private static UbiArtVersionProfile CreateProfile(
        UbiArtPlatform platform,
        UbiArtEngineVersion rootVersion,
        UbiArtEngineVersion engineVersion)
    {
        if (platform == UbiArtPlatform.Uncooked)
        {
            IUbiArtLayout layout = rootVersion switch
            {
                UbiArtEngineVersion.JD2014 => new JD2014LayoutResolver(),
                UbiArtEngineVersion.JD2015 => new JD2015LayoutResolver(),
                _ => new UbiArtLayoutResolver()
            };
            return new(platform, engineVersion, layout, new LuaUbiArtSerializer());
        }

        return rootVersion switch
        {
            UbiArtEngineVersion.JD2014 => new(platform, engineVersion, new JD2014LayoutResolver(), new BinaryUbiArtSerializer(engineVersion)),
            UbiArtEngineVersion.JD2015 => new(platform, engineVersion, new JD2015LayoutResolver(), new BinaryUbiArtSerializer(engineVersion)),
            _ => new(
                platform,
                engineVersion,
                new UbiArtLayoutResolver(),
                UsesLegacyBinarySerializer(platform, engineVersion)
                    ? new BinaryUbiArtSerializer(engineVersion)
                    : new JsonUbiArtSerializer())
        };
    }

    private static bool UsesLegacyBinarySerializer(UbiArtPlatform platform, UbiArtEngineVersion engineVersion) =>
        platform is UbiArtPlatform.Revolution or UbiArtPlatform.Cell or UbiArtPlatform.Xenon &&
        engineVersion is >= UbiArtEngineVersion.JD2016 and <= UbiArtEngineVersion.JD2020;
}
