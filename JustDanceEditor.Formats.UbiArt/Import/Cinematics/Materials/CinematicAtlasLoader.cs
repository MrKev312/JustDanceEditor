using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class CinematicAtlasLoader
{
    private const int AtlasVersion = 18;
    private const string TextAtlasMarker = "ITF_GFX_UV_ATLAS";
    private const string NumberPattern = @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?";

    public static CinematicAtlasContainer LoadDefault(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        if (TryGetMissingDlcAtlasContainerPath(fileSystem, out string? missingAtlasPath))
        {
            throw new FileNotFoundException(
                $"The selected UbiArt DLC package references atlas-backed textures, but its atlas container was not found. Expected '{missingAtlasPath}'. Keep the DLC IPK or extracted IPK folder together with atlascontainer.ckd, secure_fat.gf, sgscontainer.ckd, and dlcdescriptor.ckd.",
                missingAtlasPath);
        }

        List<AtlasContainerBytes> sources = [.. ReadDefaultAtlasContainers(fileSystem)];
        if (sources.Count == 0)
        {
            logger.LogDebug("Cinematic atlascontainer was not found; material geometry will use no-atlas fallback.");
            return CinematicAtlasContainer.Empty;
        }

        Dictionary<uint, CinematicAtlas> mergedAtlases = [];
        int loadedCount = 0;
        foreach (AtlasContainerBytes source in sources)
        {
            try
            {
                CinematicAtlasContainer container = Read(source.Bytes);
                foreach ((uint atlasId, CinematicAtlas atlas) in container.Atlases)
                    mergedAtlases.TryAdd(atlasId, atlas);

                loadedCount++;
                logger.LogDebug(
                    "Loaded cinematic atlascontainer '{AtlasContainerPath}' with {AtlasCount} atlas record(s).",
                    source.SourcePath,
                    container.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Could not read cinematic atlascontainer '{AtlasContainerPath}'.",
                    source.SourcePath);
            }
        }

        if (mergedAtlases.Count == 0)
        {
            logger.LogDebug("No readable cinematic atlascontainer records were found; material geometry will use no-atlas fallback.");
            return CinematicAtlasContainer.Empty;
        }

        if (loadedCount > 1)
        {
            logger.LogDebug(
                "Merged {AtlasContainerCount} cinematic atlascontainer file(s) into {AtlasCount} atlas record(s).",
                loadedCount,
                mergedAtlases.Count);
        }

        return new CinematicAtlasContainer(mergedAtlases);
    }

    public static CinematicAtlasContainer Read(byte[] bytes)
    {
        CinematicBinaryReader reader = new(bytes);
        reader.SkipUInt32();
        int atlasCount = reader.ReadInt32();
        if (atlasCount is < 0 or > 65536)
            throw new InvalidDataException($"Invalid legacy atlas container count {atlasCount} at 0x{reader.Offset - 4:X}.");

        Dictionary<uint, CinematicAtlas> parsedAtlases = new(atlasCount);
        for (int i = 0; i < atlasCount; i++)
        {
            uint atlasId = reader.ReadUInt32();
            parsedAtlases[atlasId] = ReadAtlas(reader);
        }

        return new CinematicAtlasContainer(parsedAtlases);
    }

    public static bool TryReadLooseAtlas(
        byte[] bytes,
        [NotNullWhen(true)] out CinematicAtlas? atlas)
    {
        if (TryReadTextAtlas(bytes, out atlas))
            return true;

        try
        {
            atlas = ReadSingleAtlas(bytes);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            atlas = null;
            return false;
        }
    }

    internal static CinematicAtlas ReadSingleAtlas(byte[] bytes)
    {
        CinematicBinaryReader reader = new(bytes);
        return ReadAtlas(reader);
    }

    private static CinematicAtlas ReadAtlas(CinematicBinaryReader reader)
    {
        int version = reader.ReadInt32();
        if (version != AtlasVersion)
            throw new InvalidDataException($"Unsupported legacy atlas version {version} at 0x{reader.Offset - 4:X}.");

        float width = reader.ReadSingle();
        float height = reader.ReadSingle();
        Dictionary<int, CinematicUvData> uvMap = ReadIntMap(reader, ReadUvData);
        Dictionary<int, CinematicUvParameters> uvParameters = ReadOptionalUvParametersMap(reader, uvMap.Count);
        return new CinematicAtlas(width, height, uvMap, uvParameters);
    }

    private static bool TryReadTextAtlas(
        byte[] bytes,
        [NotNullWhen(true)] out CinematicAtlas? atlas)
    {
        atlas = null;
        if (bytes.Length < TextAtlasMarker.Length)
            return false;

        string text = Encoding.UTF8.GetString(bytes);
        if (!text.Contains(TextAtlasMarker, StringComparison.Ordinal))
            return false;

        float textureHeight = ReadTextFloat(text, "TextureHeight") ?? 0;
        float textureAspectRatio = ReadTextFloat(text, "TextureAspectRatio") ?? 1;
        if (textureHeight <= 0 || textureAspectRatio <= 0)
            return false;

        Dictionary<int, CinematicUvData> uvMap = [];
        foreach (Match blockMatch in Regex.Matches(text, @"\{(?<body>[^{}]*)\}", RegexOptions.Singleline))
        {
            string body = blockMatch.Groups["body"].Value;
            int? index = ReadTextInt(body, "index");
            int? uvNumber = ReadTextInt(body, "uvNumber");
            if (index == null || uvNumber is null or <= 0 or > 16384)
                continue;

            Vector2[] uvs = new Vector2[uvNumber.Value];
            bool complete = true;
            for (int uvIndex = 0; uvIndex < uvs.Length; uvIndex++)
            {
                if (!TryReadTextUv(body, uvIndex, out Vector2 uv))
                {
                    complete = false;
                    break;
                }

                uvs[uvIndex] = uv;
            }

            if (complete)
                uvMap[index.Value] = new CinematicUvData(uvs);
        }

        if (uvMap.Count == 0)
            return false;

        atlas = new CinematicAtlas(
            textureHeight * textureAspectRatio,
            textureHeight,
            uvMap,
            new Dictionary<int, CinematicUvParameters>());
        return true;
    }

    private static int? ReadTextInt(string text, string name)
    {
        Match match = Regex.Match(text, $@"\b{Regex.Escape(name)}\s*=\s*(?<value>[-+]?\d+)", RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        return int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static float? ReadTextFloat(string text, string name)
    {
        Match match = Regex.Match(
            text,
            $@"\b{Regex.Escape(name)}\s*=\s*(?<value>{NumberPattern})",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        return float.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : null;
    }

    private static bool TryReadTextUv(string text, int index, out Vector2 uv)
    {
        uv = default;
        Match match = Regex.Match(
            text,
            $@"\buv{index}\s*=\s*vector2dNew\(\s*(?<x>{NumberPattern})\s*,\s*(?<y>{NumberPattern})\s*\)",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        if (!float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        {
            return false;
        }

        uv = new Vector2(x, y);
        return true;
    }

    private static Dictionary<int, CinematicUvParameters> ReadOptionalUvParametersMap(
        CinematicBinaryReader reader,
        int uvMapCount)
    {
        int startOffset = reader.Offset;
        if (reader.Remaining < sizeof(int))
            return [];

        try
        {
            int count = reader.PeekInt32();
            if (count < 0 || count > uvMapCount)
                return [];

            return ReadIntMap(reader, ReadUvParameters);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            reader.Offset = startOffset;
            return [];
        }
    }

    private static Dictionary<int, T> ReadIntMap<T>(
        CinematicBinaryReader reader,
        Func<CinematicBinaryReader, T> readValue)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 65536)
            throw new InvalidDataException($"Invalid legacy atlas map count {count} at 0x{reader.Offset - 4:X}.");

        Dictionary<int, T> values = new(count);
        for (int i = 0; i < count; i++)
        {
            int key = reader.ReadInt32();
            values[key] = readValue(reader);
        }

        return values;
    }

    private static CinematicUvData ReadUvData(CinematicBinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 16384)
            throw new InvalidDataException($"Invalid legacy UV count {count} at 0x{reader.Offset - 4:X}.");

        Vector2[] uvs = new Vector2[count];
        for (int i = 0; i < count; i++)
            uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());

        return new CinematicUvData(uvs);
    }

    private static CinematicUvParameters ReadUvParameters(CinematicBinaryReader reader)
    {
        float orientation = reader.ReadSingle();
        string flag = ReadString8(reader);
        CinematicUvParameter[] parameters = ReadVector(reader, ReadUvParameter);
        CinematicUvTriangle[] triangles = ReadVector(reader, ReadTriangle);
        bool hasWeight = reader.ReadUInt32() != 0;
        bool hasColor = reader.ReadUInt32() != 0;

        return new CinematicUvParameters(orientation, flag, parameters, triangles, hasWeight, hasColor);
    }

    private static CinematicUvParameter ReadUvParameter(CinematicBinaryReader reader) =>
        new(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt32(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

    private static CinematicUvTriangle ReadTriangle(CinematicBinaryReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    private static T[] ReadVector<T>(
        CinematicBinaryReader reader,
        Func<CinematicBinaryReader, T> readValue)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 65536)
            throw new InvalidDataException($"Invalid legacy vector count {count} at 0x{reader.Offset - 4:X}.");

        T[] values = new T[count];
        for (int i = 0; i < count; i++)
            values[i] = readValue(reader);

        return values;
    }

    private static string ReadString8(CinematicBinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length is < 0 or > 4096)
            throw new InvalidDataException($"Invalid legacy String8 length {length} at 0x{reader.Offset - 4:X}.");

        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static IEnumerable<AtlasContainerBytes> ReadDefaultAtlasContainers(JustDanceUbiArtFileSystem fileSystem)
    {
        HashSet<string> seenSources = new(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in GetLayeredAtlasCandidates(fileSystem))
        {
            if (!fileSystem.GetFilePath(relativePath, out CookedFile? atlasFile))
                continue;

            string sourcePath = atlasFile.RelativePath;
            if (!seenSources.Add(sourcePath))
                continue;

            using Stream stream = fileSystem.GetFileStream(atlasFile);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            yield return new AtlasContainerBytes(memory.ToArray(), sourcePath);
        }

        foreach (string physicalPath in GetPhysicalAtlasCandidates(fileSystem))
        {
            if (!File.Exists(physicalPath))
                continue;

            string sourcePath = Path.GetFullPath(physicalPath);
            if (!seenSources.Add(sourcePath))
                continue;

            yield return new AtlasContainerBytes(File.ReadAllBytes(physicalPath), sourcePath);
        }
    }

    private static IEnumerable<string> GetLayeredAtlasCandidates(JustDanceUbiArtFileSystem fileSystem)
    {
        string platformFolder = GetPlatformFolder(fileSystem);
        yield return "atlascontainer";
        yield return "atlascontainer.ckd";
        yield return Path.Combine("cache", "itf_cooked", platformFolder, "atlascontainer.ckd");

        foreach (string dlcName in GetDlcAtlasContainerNames(fileSystem))
        {
            yield return Path.Combine(dlcName, "atlascontainer");
            yield return Path.Combine(dlcName, "atlascontainer.ckd");
            yield return Path.Combine("cache", "itf_cooked", platformFolder, dlcName, "atlascontainer.ckd");
        }
    }

    private static IEnumerable<string> GetPhysicalAtlasCandidates(JustDanceUbiArtFileSystem fileSystem)
    {
        string platformFolder = GetPlatformFolder(fileSystem);
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        string inputRoot = Path.GetFullPath(fileSystem.InputFolders.InputFolder);
        roots.Add(inputRoot);

        DirectoryInfo? inputDirectory = Directory.Exists(inputRoot)
            ? new DirectoryInfo(inputRoot)
            : new FileInfo(inputRoot).Directory;
        if (inputDirectory != null && inputDirectory.Exists)
            roots.Add(inputDirectory.FullName);

        DirectoryInfo? parentDirectory = inputDirectory?.Parent;
        if (parentDirectory != null && parentDirectory.Exists)
        {
            roots.Add(parentDirectory.FullName);
            foreach (DirectoryInfo sibling in parentDirectory.EnumerateDirectories("*Logic*", SearchOption.TopDirectoryOnly))
                roots.Add(sibling.FullName);

            if (inputDirectory != null)
            {
                string bundleLogicName = inputDirectory.Name
                    .Replace("Bundle_0", "BundleLogic", StringComparison.OrdinalIgnoreCase)
                    .Replace("Bundle", "BundleLogic", StringComparison.OrdinalIgnoreCase);
                string bundleLogicPath = Path.Combine(parentDirectory.FullName, bundleLogicName);
                if (Directory.Exists(bundleLogicPath))
                    roots.Add(bundleLogicPath);
            }
        }

        foreach (string root in roots)
        {
            yield return Path.Combine(root, "cache", "itf_cooked", platformFolder, "atlascontainer.ckd");
            yield return Path.Combine(root, "atlascontainer.ckd");

            foreach (string dlcName in GetDlcAtlasContainerNames(fileSystem))
            {
                yield return Path.Combine(root, dlcName, "cache", "itf_cooked", platformFolder, "atlascontainer.ckd");
                yield return Path.Combine(root, dlcName, "atlascontainer.ckd");
                yield return Path.Combine(root, "cache", "itf_cooked", platformFolder, dlcName, "atlascontainer.ckd");
            }
        }
    }

    private static bool TryGetMissingDlcAtlasContainerPath(
        JustDanceUbiArtFileSystem fileSystem,
        [NotNullWhen(true)] out string? missingAtlasPath)
    {
        missingAtlasPath = null;
        string inputRoot = Path.GetFullPath(fileSystem.InputFolders.InputFolder);
        string? packageDirectory = null;

        if (Path.GetExtension(inputRoot).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            string inputName = Path.GetFileNameWithoutExtension(inputRoot);
            DirectoryInfo? parent = new FileInfo(inputRoot).Directory;
            if (parent != null &&
                (IsDlcMainSceneName(inputName) ||
                 File.Exists(Path.Combine(parent.FullName, "dlcdescriptor.ckd"))))
            {
                packageDirectory = parent.FullName;
            }
        }
        else if (Directory.Exists(inputRoot))
        {
            string inputName = new DirectoryInfo(inputRoot).Name;
            DirectoryInfo? parent = new DirectoryInfo(inputRoot).Parent;
            if (File.Exists(Path.Combine(inputRoot, "dlcdescriptor.ckd")))
            {
                packageDirectory = inputRoot;
            }
            else if (parent != null && IsDlcMainSceneName(inputName))
            {
                packageDirectory = parent.FullName;
            }
        }

        if (packageDirectory == null)
            return false;

        string expectedAtlas = Path.Combine(packageDirectory, "atlascontainer.ckd");
        if (File.Exists(expectedAtlas))
            return false;

        missingAtlasPath = expectedAtlas;
        return true;
    }

    private static bool IsDlcMainSceneName(string name) =>
        name.Contains("dlc", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("_main_scene_", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetDlcAtlasContainerNames(JustDanceUbiArtFileSystem fileSystem)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        AddDlcAtlasContainerName(names, fileSystem.SongName);

        string inputRoot = fileSystem.InputFolders.InputFolder;
        string? inputName = Directory.Exists(inputRoot)
            ? new DirectoryInfo(inputRoot).Name
            : Path.GetFileNameWithoutExtension(inputRoot);
        AddDlcAtlasContainerName(names, inputName);

        foreach (string name in names)
            yield return name;
    }

    private static void AddDlcAtlasContainerName(HashSet<string> names, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        string normalized = CinematicPath.Normalize(name);
        if (!string.IsNullOrWhiteSpace(normalized))
            names.Add(normalized);
    }

    private static string GetPlatformFolder(JustDanceUbiArtFileSystem fileSystem) =>
        fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked
            ? "wiiu"
            : fileSystem.VersionProfile.Platform.GetCookedFolderName();

    private sealed record AtlasContainerBytes(byte[] Bytes, string SourcePath);
}
