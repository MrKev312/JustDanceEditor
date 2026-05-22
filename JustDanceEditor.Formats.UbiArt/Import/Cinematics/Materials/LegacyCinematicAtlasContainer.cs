using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal sealed class LegacyCinematicAtlasContainer(IReadOnlyDictionary<uint, LegacyCinematicAtlas> atlases)
{
    private const int AtlasVersion = 18;

    public int Count => atlases.Count;

    public static LegacyCinematicAtlasContainer Empty { get; } = new(new Dictionary<uint, LegacyCinematicAtlas>());

    public static LegacyCinematicAtlasContainer LoadDefault(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        if (!TryReadDefaultAtlasContainer(fileSystem, out byte[]? bytes, out string? sourcePath) || bytes == null)
        {
            logger.LogDebug("Legacy cinematic atlascontainer was not found; material geometry will use no-atlas fallback.");
            return Empty;
        }

        try
        {
            LegacyCinematicAtlasContainer container = Read(bytes);
            logger.LogDebug(
                "Loaded legacy cinematic atlascontainer '{AtlasContainerPath}' with {AtlasCount} atlas record(s).",
                sourcePath,
                container.Count);
            return container;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not read legacy cinematic atlascontainer '{AtlasContainerPath}'; material geometry will use no-atlas fallback.",
                sourcePath);
            return Empty;
        }
    }

    public static LegacyCinematicAtlasContainer Read(byte[] bytes)
    {
        LegacyCinematicBinaryReader reader = new(bytes);
        _ = reader.ReadUInt32();
        int atlasCount = reader.ReadInt32();
        if (atlasCount is < 0 or > 65536)
            throw new InvalidDataException($"Invalid legacy atlas container count {atlasCount} at 0x{reader.Offset - 4:X}.");

        Dictionary<uint, LegacyCinematicAtlas> parsedAtlases = new(atlasCount);
        for (int i = 0; i < atlasCount; i++)
        {
            uint atlasId = reader.ReadUInt32();
            parsedAtlases[atlasId] = ReadAtlas(reader);
        }

        return new LegacyCinematicAtlasContainer(parsedAtlases);
    }

    public bool TryCreateGeometry(
        string texturePath,
        int atlasIndex,
        out RenderGeometry geometry,
        out string atlasPath)
    {
        if (!TryGetAtlasForTexture(texturePath, out LegacyCinematicAtlas? atlas, out atlasPath))
        {
            geometry = RenderGeometry.NoAtlasQuad;
            return false;
        }

        return TryCreateGeometry(atlas, atlasIndex, out geometry);
    }

    public bool TryGetAtlasForTexture(
        string texturePath,
        [NotNullWhen(true)] out LegacyCinematicAtlas? atlas,
        out string atlasPath)
    {
        atlasPath = GetAtlasPathForTexture(texturePath);
        uint atlasId = LegacyCinematicStringId.Compute(atlasPath);
        if (atlases.TryGetValue(atlasId, out atlas))
            return true;

        return atlases.TryGetValue(LegacyCinematicStringId.Compute(LegacyCinematicPath.Normalize(texturePath)), out atlas);
    }

    public bool TryGetAtlas(string atlasPath, out LegacyCinematicAtlas? atlas) =>
        atlases.TryGetValue(LegacyCinematicStringId.Compute(LegacyCinematicPath.Normalize(atlasPath)), out atlas);

    internal static string GetAtlasPathForTexture(string texturePath)
    {
        string normalized = LegacyCinematicPath.Normalize(texturePath);
        int slash = normalized.LastIndexOf('/');
        int dot = normalized.LastIndexOf('.');
        if (dot <= slash)
            return $"{normalized}.atl";

        return $"{normalized[..dot]}.atl";
    }

    internal static bool TryCreateGeometry(
        LegacyCinematicAtlas atlas,
        int atlasIndex,
        out RenderGeometry geometry)
    {
        geometry = RenderGeometry.NoAtlasQuad;
        if (!TryResolveUvData(atlas, atlasIndex, out int resolvedIndex, out LegacyCinematicUvData? uvData) ||
            uvData.Uvs.Count == 0)
        {
            return false;
        }

        atlas.UvParameters.TryGetValue(resolvedIndex, out LegacyCinematicUvParameters? uvParameters);
        if (uvData.Uvs.Count == 2)
        {
            geometry = RenderGeometry.FromRectangleAtlas(uvData.Uvs[0], uvData.Uvs[1], uvParameters);
            return true;
        }

        Vector2 center = uvParameters?.Triangles.Count > 0
            ? GetMeshCenter(uvData.Uvs)
            : GetBarycentre(uvData.Uvs);

        List<Vector2> triangulationPositions = new(uvData.Uvs.Count);
        List<RenderVertex> vertices = new(uvData.Uvs.Count);
        for (int i = 0; i < uvData.Uvs.Count; i++)
        {
            Vector2 uv = uvData.Uvs[i];
            LegacyCinematicUvParameter? parameter = uvParameters?.Parameters.Count > i
                ? uvParameters.Parameters[i]
                : null;
            Vector2 position = new(
                (uv.X - center.X) * (float)atlas.Width,
                (center.Y - uv.Y) * (float)atlas.Height);
            triangulationPositions.Add(position);
            vertices.Add(new RenderVertex(
                position.X + (parameter?.OffsetX ?? 0),
                position.Y + (parameter?.OffsetY ?? 0),
                parameter?.Depth ?? 0,
                uv.X,
                uv.Y,
                uvParameters?.HasWeight == true
                    ? parameter?.Weight ?? 0
                    : 0,
                uvParameters?.HasWeight == true
                    ? (parameter?.OffsetSinus ?? 0) * Math.PI
                    : 0));
        }

        int[] indices = uvParameters?.Triangles.Count > 0
            ? [.. uvParameters.Triangles.SelectMany(triangle => new[] { triangle.Index0, triangle.Index1, triangle.Index2 })]
            : CreateNgonIndices(triangulationPositions);
        if (indices.Length < 3)
            return false;

        geometry = RenderGeometry.FromMesh(vertices, indices, CinematicGeometrySource.MeshAtlas);
        return true;
    }

    internal static bool TryGetUvRect(
        LegacyCinematicAtlas atlas,
        int atlasIndex,
        out LegacyCinematicUvRect uvRect)
    {
        uvRect = LegacyCinematicUvRect.Full;
        if (!TryResolveUvData(atlas, atlasIndex, out _, out LegacyCinematicUvData? uvData) ||
            uvData.Uvs.Count < 2)
        {
            return false;
        }

        Vector2 uv0 = uvData.Uvs[0];
        Vector2 uv1 = uvData.Uvs[1];
        uvRect = new LegacyCinematicUvRect(uv0.X, uv0.Y, uv1.X, uv1.Y);
        return true;
    }

    private static bool TryResolveUvData(
        LegacyCinematicAtlas atlas,
        int atlasIndex,
        out int resolvedIndex,
        [NotNullWhen(true)] out LegacyCinematicUvData? uvData)
    {
        uvData = null;
        resolvedIndex = 0;
        if (atlas.UvMap.Count == 0)
            return false;

        resolvedIndex = Math.Clamp(atlasIndex, 0, atlas.UvMap.Count - 1);
        if (atlas.UvMap.TryGetValue(resolvedIndex, out uvData))
            return true;

        uvData = atlas.UvMap
            .OrderBy(pair => pair.Key)
            .Skip(resolvedIndex)
            .Select(pair => pair.Value)
            .FirstOrDefault();
        return uvData != null;
    }

    private static LegacyCinematicAtlas ReadAtlas(LegacyCinematicBinaryReader reader)
    {
        int version = reader.ReadInt32();
        if (version != AtlasVersion)
            throw new InvalidDataException($"Unsupported legacy atlas version {version} at 0x{reader.Offset - 4:X}.");

        float width = reader.ReadSingle();
        float height = reader.ReadSingle();
        Dictionary<int, LegacyCinematicUvData> uvMap = ReadIntMap(reader, ReadUvData);
        Dictionary<int, LegacyCinematicUvParameters> uvParameters = ReadIntMap(reader, ReadUvParameters);
        return new LegacyCinematicAtlas(width, height, uvMap, uvParameters);
    }

    private static Dictionary<int, T> ReadIntMap<T>(
        LegacyCinematicBinaryReader reader,
        Func<LegacyCinematicBinaryReader, T> readValue)
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

    private static LegacyCinematicUvData ReadUvData(LegacyCinematicBinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 16384)
            throw new InvalidDataException($"Invalid legacy UV count {count} at 0x{reader.Offset - 4:X}.");

        Vector2[] uvs = new Vector2[count];
        for (int i = 0; i < count; i++)
            uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());

        return new LegacyCinematicUvData(uvs);
    }

    private static LegacyCinematicUvParameters ReadUvParameters(LegacyCinematicBinaryReader reader)
    {
        float orientation = reader.ReadSingle();
        string flag = ReadString8(reader);
        LegacyCinematicUvParameter[] parameters = ReadVector(reader, ReadUvParameter);
        LegacyCinematicUvTriangle[] triangles = ReadVector(reader, ReadTriangle);
        bool hasWeight = reader.ReadUInt32() != 0;
        bool hasColor = reader.ReadUInt32() != 0;

        return new LegacyCinematicUvParameters(orientation, flag, parameters, triangles, hasWeight, hasColor);
    }

    private static LegacyCinematicUvParameter ReadUvParameter(LegacyCinematicBinaryReader reader) =>
        new(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt32(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

    private static LegacyCinematicUvTriangle ReadTriangle(LegacyCinematicBinaryReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    private static T[] ReadVector<T>(
        LegacyCinematicBinaryReader reader,
        Func<LegacyCinematicBinaryReader, T> readValue)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 65536)
            throw new InvalidDataException($"Invalid legacy vector count {count} at 0x{reader.Offset - 4:X}.");

        T[] values = new T[count];
        for (int i = 0; i < count; i++)
            values[i] = readValue(reader);

        return values;
    }

    private static string ReadString8(LegacyCinematicBinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length is < 0 or > 4096)
            throw new InvalidDataException($"Invalid legacy String8 length {length} at 0x{reader.Offset - 4:X}.");

        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static Vector2 GetBarycentre(IReadOnlyList<Vector2> uvs)
    {
        Vector2 center = uvs[0];
        for (int i = 1; i < uvs.Count; i++)
            center += uvs[i];

        return center / uvs.Count;
    }

    private static Vector2 GetMeshCenter(IReadOnlyList<Vector2> uvs)
    {
        float xMin = uvs[0].X;
        float xMax = uvs[0].X;
        float yMin = uvs[0].Y;
        float yMax = uvs[0].Y;

        for (int i = 1; i < uvs.Count; i++)
        {
            xMin = Math.Min(xMin, uvs[i].X);
            xMax = Math.Max(xMax, uvs[i].X);
            yMin = Math.Min(yMin, uvs[i].Y);
            yMax = Math.Max(yMax, uvs[i].Y);
        }

        return new Vector2((xMax - xMin) * 0.5f, (yMax - yMin) * 0.5f);
    }

    private static int[] CreateNgonIndices(IReadOnlyList<Vector2> positions)
    {
        if (positions.Count < 3)
            return [];

        List<int> remaining = [.. Enumerable.Range(0, positions.Count)];
        List<int> indices = new((positions.Count - 2) * 3);
        bool counterClockwise = GetSignedArea(positions) > 0;
        int guard = positions.Count * positions.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int previousIndex = remaining[(i + remaining.Count - 1) % remaining.Count];
                int currentIndex = remaining[i];
                int nextIndex = remaining[(i + 1) % remaining.Count];
                Vector2 previous = positions[previousIndex];
                Vector2 current = positions[currentIndex];
                Vector2 next = positions[nextIndex];

                if (!IsConvex(previous, current, next, counterClockwise) ||
                    ContainsOtherPointInTriangle(positions, remaining, previousIndex, currentIndex, nextIndex, counterClockwise))
                {
                    continue;
                }

                indices.Add(previousIndex);
                indices.Add(currentIndex);
                indices.Add(nextIndex);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
                return [];
        }

        if (remaining.Count == 3)
        {
            indices.Add(remaining[0]);
            indices.Add(remaining[1]);
            indices.Add(remaining[2]);
        }

        return [.. indices];
    }

    private static float GetSignedArea(IReadOnlyList<Vector2> positions)
    {
        double area = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector2 current = positions[i];
            Vector2 next = positions[(i + 1) % positions.Count];
            area += ((double)current.X * next.Y) - ((double)next.X * current.Y);
        }

        return (float)(area * 0.5);
    }

    private static bool IsConvex(Vector2 previous, Vector2 current, Vector2 next, bool counterClockwise)
    {
        float cross = Cross(previous, current, next);
        return counterClockwise
            ? cross > 0.000001f
            : cross < -0.000001f;
    }

    private static bool ContainsOtherPointInTriangle(
        IReadOnlyList<Vector2> positions,
        IReadOnlyList<int> remaining,
        int index0,
        int index1,
        int index2,
        bool counterClockwise)
    {
        Vector2 a = positions[index0];
        Vector2 b = positions[index1];
        Vector2 c = positions[index2];
        foreach (int index in remaining)
        {
            if (index == index0 || index == index1 || index == index2)
                continue;

            if (IsPointInTriangle(positions[index], a, b, c, counterClockwise))
                return true;
        }

        return false;
    }

    private static bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, bool counterClockwise)
    {
        float cross0 = Cross(a, b, point);
        float cross1 = Cross(b, c, point);
        float cross2 = Cross(c, a, point);
        const float epsilon = -0.000001f;
        return counterClockwise
            ? cross0 >= epsilon && cross1 >= epsilon && cross2 >= epsilon
            : cross0 <= -epsilon && cross1 <= -epsilon && cross2 <= -epsilon;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static bool TryReadDefaultAtlasContainer(
        JustDanceUbiArtFileSystem fileSystem,
        out byte[]? bytes,
        out string? sourcePath)
    {
        foreach (string relativePath in GetLayeredAtlasCandidates(fileSystem))
        {
            if (!fileSystem.GetFilePath(relativePath, out CookedFile? atlasFile))
                continue;

            using Stream stream = fileSystem.GetFileStream(atlasFile);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
            sourcePath = relativePath;
            return true;
        }

        foreach (string physicalPath in GetPhysicalAtlasCandidates(fileSystem))
        {
            if (!File.Exists(physicalPath))
                continue;

            bytes = File.ReadAllBytes(physicalPath);
            sourcePath = physicalPath;
            return true;
        }

        bytes = null;
        sourcePath = null;
        return false;
    }

    private static IEnumerable<string> GetLayeredAtlasCandidates(JustDanceUbiArtFileSystem fileSystem)
    {
        string platformFolder = GetPlatformFolder(fileSystem);
        yield return "atlascontainer";
        yield return "atlascontainer.ckd";
        yield return Path.Combine("cache", "itf_cooked", platformFolder, "atlascontainer.ckd");
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
        }
    }

    private static string GetPlatformFolder(JustDanceUbiArtFileSystem fileSystem) =>
        fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked
            ? "wiiu"
            : fileSystem.VersionProfile.Platform.GetCookedFolderName();
}

internal static class LegacyCinematicPath
{
    public static string Normalize(string path)
    {
        string normalized = LegacyCinematicNames.NormalizePath(path).Replace('\\', '/').ToLowerInvariant();
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        return normalized;
    }
}

internal static class LegacyCinematicStringId
{
    public static uint Compute(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return Compute(bytes);
    }

    private static uint Compute(ReadOnlySpan<byte> bytes)
    {
        unchecked
        {
            uint originalLength = (uint)bytes.Length;
            uint length = originalLength;
            uint a = 0x9E3779B9;
            uint b = a;
            uint c = 0;
            int offset = 0;

            while (length >= 12)
            {
                a += ToUpperAscii(bytes[offset]) |
                    (ToUpperAscii(bytes[offset + 1]) << 8) |
                    (ToUpperAscii(bytes[offset + 2]) << 16) |
                    (ToUpperAscii(bytes[offset + 3]) << 24);
                b += ToUpperAscii(bytes[offset + 4]) |
                    (ToUpperAscii(bytes[offset + 5]) << 8) |
                    (ToUpperAscii(bytes[offset + 6]) << 16) |
                    (ToUpperAscii(bytes[offset + 7]) << 24);
                c += ToUpperAscii(bytes[offset + 8]) |
                    (ToUpperAscii(bytes[offset + 9]) << 8) |
                    (ToUpperAscii(bytes[offset + 10]) << 16) |
                    (ToUpperAscii(bytes[offset + 11]) << 24);
                Mix(ref a, ref b, ref c);
                offset += 12;
                length -= 12;
            }

            c += originalLength;
            switch (length)
            {
                case 11:
                    c += ToUpperAscii(bytes[offset + 10]) << 24;
                    goto case 10;
                case 10:
                    c += ToUpperAscii(bytes[offset + 9]) << 16;
                    goto case 9;
                case 9:
                    c += ToUpperAscii(bytes[offset + 8]) << 8;
                    goto case 8;
                case 8:
                    b += ToUpperAscii(bytes[offset + 7]) << 24;
                    goto case 7;
                case 7:
                    b += ToUpperAscii(bytes[offset + 6]) << 16;
                    goto case 6;
                case 6:
                    b += ToUpperAscii(bytes[offset + 5]) << 8;
                    goto case 5;
                case 5:
                    b += ToUpperAscii(bytes[offset + 4]);
                    goto case 4;
                case 4:
                    a += ToUpperAscii(bytes[offset + 3]) << 24;
                    goto case 3;
                case 3:
                    a += ToUpperAscii(bytes[offset + 2]) << 16;
                    goto case 2;
                case 2:
                    a += ToUpperAscii(bytes[offset + 1]) << 8;
                    goto case 1;
                case 1:
                    a += ToUpperAscii(bytes[offset]);
                    break;
            }

            Mix(ref a, ref b, ref c);
            return c;
        }
    }

    private static uint ToUpperAscii(byte value) =>
        value is >= (byte)'a' and <= (byte)'z'
            ? (uint)(value + 'A' - 'a')
            : value;

    private static void Mix(ref uint a, ref uint b, ref uint c)
    {
        unchecked
        {
            a -= b;
            a -= c;
            a ^= c >> 13;
            b -= c;
            b -= a;
            b ^= a << 8;
            c -= a;
            c -= b;
            c ^= b >> 13;
            a -= b;
            a -= c;
            a ^= c >> 12;
            b -= c;
            b -= a;
            b ^= a << 16;
            c -= a;
            c -= b;
            c ^= b >> 5;
            a -= b;
            a -= c;
            a ^= c >> 3;
            b -= c;
            b -= a;
            b ^= a << 10;
            c -= a;
            c -= b;
            c ^= b >> 15;
        }
    }
}