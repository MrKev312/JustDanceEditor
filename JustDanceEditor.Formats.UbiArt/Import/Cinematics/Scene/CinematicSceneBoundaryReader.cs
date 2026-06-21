using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicSceneBoundaryReader
{
    internal static bool IsActorBoundaryAt(byte[] bytes, int offset)
    {
        if (offset == bytes.Length)
            return true;

        if (offset < 0 || offset + 4 > bytes.Length)
            return false;

        uint nextActorType = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return LegacyBinaryTypeRegistry.TryResolve(typeof(CinematicTypedPickableActorBinary), nextActorType, out _);
    }

    internal static void SnapToNearbyActorBoundary(CinematicBinaryReader reader, int forwardSearchLength)
    {
        if (CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            return;

        int searchEndOffset = Math.Min(reader.Bytes.Length - 4, reader.Offset + forwardSearchLength);
        for (int offset = reader.Offset; offset <= searchEndOffset; offset++)
        {
            if (!CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                continue;

            reader.Offset = offset;
            return;
        }
    }

    internal static void SnapSceneActorListStartToNextBoundary(
        CinematicBinaryReader reader,
        int depth,
        IReadOnlyList<string> parentPath,
        ILogger logger,
        bool debugScene)
    {
        if (CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            return;

        int payloadOffset = reader.Offset;
        int? actorBoundaryOffset = CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(reader.Bytes, payloadOffset);
        if (actorBoundaryOffset is int offset)
        {
            reader.Offset = offset;
            if (debugScene)
            {
                logger.LogInformation(
                    "Legacy scene actor-list prefix skipped depth={Depth} parent={ParentPath} start=0x{Start:X} actorsStart=0x{ActorsStart:X} bytes={ByteCount}",
                    depth,
                    string.Join("/", parentPath),
                    payloadOffset,
                    offset,
                    offset - payloadOffset);
            }

            return;
        }
    }

    internal static int? FindNextActorBoundaryOffset(byte[] bytes, int startOffset)
    {
        int searchEndOffset = bytes.Length - 4;
        for (int offset = startOffset; offset <= searchEndOffset; offset++)
        {
            if (CinematicSceneBoundaryReader.IsActorBoundaryAt(bytes, offset))
                return offset;
        }

        return null;
    }

    internal static void SnapEmbeddedSceneTailToNextActorBoundary(
        CinematicBinaryReader reader,
        int depth,
        IReadOnlyList<string> parentPath,
        ILogger logger,
        bool debugScene)
    {
        if (CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            return;

        int tailOffset = reader.Offset;
        int searchEndOffset = reader.Bytes.Length - 4;
        for (int offset = tailOffset; offset <= searchEndOffset; offset++)
        {
            if (!CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                continue;

            reader.Offset = offset;
            if (debugScene)
            {
                logger.LogInformation(
                    "Legacy embedded scene tail skipped depth={Depth} parent={ParentPath} start=0x{Start:X} end=0x{End:X} bytes={ByteCount}",
                    depth,
                    string.Join("/", parentPath),
                    tailOffset,
                    offset,
                    offset - tailOffset);
            }

            return;
        }

        if (debugScene && tailOffset != reader.Bytes.Length)
        {
            logger.LogInformation(
                "Legacy embedded scene tail had no following actor boundary depth={Depth} parent={ParentPath} start=0x{Start:X} remaining={ByteCount}",
                depth,
                string.Join("/", parentPath),
                tailOffset,
                reader.Bytes.Length - tailOffset);
        }
    }

    internal static void SkipActorBoundaryZeroTail(CinematicBinaryReader reader)
    {
        int boundaryOffset = CinematicSceneBoundaryReader.GetActorBoundaryOffsetAfterZeroTail(reader);
        if (boundaryOffset > reader.Offset)
            reader.Offset = boundaryOffset;
    }

    internal static int GetActorBoundaryOffsetAfterZeroTail(CinematicBinaryReader reader)
    {
        int offset = reader.Offset;
        while (offset + 16 <= reader.Bytes.Length)
        {
            if (!CinematicSceneBoundaryReader.IsZeroBlock(reader.Bytes, offset, 12))
                return reader.Offset;

            offset += 12;
            if (CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                return offset;
        }

        return reader.Offset;
    }

    internal static bool IsZeroBlock(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || offset + length > bytes.Length)
            return false;

        for (int i = 0; i < length; i++)
        {
            if (bytes[offset + i] != 0)
                return false;
        }

        return true;
    }

    internal static RgbTint ReadComponentTint(byte[] bytes, int componentStart) =>
        new(
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + 8, 1),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + 4, 1),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart, 1));

    internal static float ReadComponentAlpha(byte[] bytes, int componentStart) =>
        Math.Clamp(CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + 12, 1), 0, 1);

    internal static CinematicSinusParameters ReadMaterialGraphicSinusParameters(byte[] bytes, int tailOffset)
    {
        if (tailOffset < 0 || tailOffset + CinematicConstants.MaterialGraphicTailLength > bytes.Length)
            return default;

        return new CinematicSinusParameters(
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + CinematicConstants.MaterialGraphicTailSinusAmplitudeXOffset, 0),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + CinematicConstants.MaterialGraphicTailSinusAmplitudeYOffset, 0),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + CinematicConstants.MaterialGraphicTailSinusAmplitudeZOffset, 0),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + CinematicConstants.MaterialGraphicTailSinusSpeedOffset, 1),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + CinematicConstants.MaterialGraphicTailAngleXOffset, 0),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + CinematicConstants.MaterialGraphicTailAngleYOffset, 0));
    }

    internal static CinematicMaterialGraphicFields ReadMaterialGraphicFields(
        byte[] bytes,
        int componentStart,
        LegacyBinarySerializerContext serializerContext)
    {
        bool compactJd2015Layout = (serializerContext.EngineVersion ?? 2014) >= 2015;
        int atlasIndexOffset = compactJd2015Layout
            ? CinematicConstants.MaterialGraphicAtlasIndexOffset2015
            : CinematicConstants.MaterialGraphicAtlasIndexOffset;
        int anchorOffset = compactJd2015Layout
            ? CinematicConstants.MaterialGraphicAnchorOffset2015
            : CinematicConstants.MaterialGraphicAnchorOffset;
        int customAnchorXOffset = compactJd2015Layout
            ? CinematicConstants.MaterialGraphicCustomAnchorXOffset2015
            : CinematicConstants.MaterialGraphicCustomAnchorXOffset;
        int customAnchorYOffset = compactJd2015Layout
            ? CinematicConstants.MaterialGraphicCustomAnchorYOffset2015
            : CinematicConstants.MaterialGraphicCustomAnchorYOffset;

        return new CinematicMaterialGraphicFields(
            CinematicSceneBoundaryReader.ReadMaterialGraphicAtlasIndexOrDefault(bytes, componentStart + atlasIndexOffset),
            CinematicSceneBoundaryReader.ReadTextureAnchorOrDefault(bytes, componentStart + anchorOffset),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + customAnchorXOffset, 0),
            CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + customAnchorYOffset, 0));
    }

    internal static float ReadMesh3DScaleZOrDefault(
        byte[] bytes,
        int componentStart,
        LegacyBinarySerializerContext serializerContext)
    {
        int offset = (serializerContext.EngineVersion ?? 2014) >= 2015
            ? CinematicConstants.Mesh3DScaleZOffset2015
            : CinematicConstants.Mesh3DScaleZOffset;
        float value = CinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + offset, 0.0f);
        return float.IsFinite(value) && Math.Abs(value) < 10_000.0f
            ? value
            : 0.0f;
    }

    internal static CinematicMeshOrientation ReadMesh3DOrientationOrDefault(
        byte[] bytes,
        int componentStart,
        int searchStartOffset,
        int searchLength)
    {
        return CinematicSceneBoundaryReader.ReadMesh3DComponentFieldsOrDefault(
            bytes,
            componentStart,
            searchStartOffset,
            searchLength).Orientation;
    }

    internal static CinematicMesh3DComponentFields ReadMesh3DComponentFieldsOrDefault(
        byte[] bytes,
        int componentStart,
        int searchStartOffset,
        int searchLength)
    {
        int searchStart = Math.Clamp(searchStartOffset, componentStart, bytes.Length);
        int searchEnd = Math.Min(bytes.Length - 48, componentStart + searchLength);
        for (int offset = searchStart; offset <= searchEnd; offset++)
        {
            if (offset > searchStart && CinematicSceneBoundaryReader.IsActorBoundaryAt(bytes, offset))
                break;

            if (CinematicSceneBoundaryReader.TryReadMesh3DOrientationAt(bytes, offset, out CinematicMeshOrientation orientation))
            {
                bool force2DRender = CinematicSceneBoundaryReader.ReadBool01OrDefault(bytes, offset + 64, false);
                return new CinematicMesh3DComponentFields(orientation, force2DRender);
            }
        }

        return default;
    }

    private static bool TryReadMesh3DOrientationAt(
        byte[] bytes,
        int offset,
        out CinematicMeshOrientation orientation)
    {
        orientation = default;
        Span<float> values = stackalloc float[9];
        int valueIndex = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                float value = CinematicSceneBoundaryReader.ReadSingleOrDefault(
                    bytes,
                    offset + (row * 16) + (column * 4),
                    float.NaN);
                if (!float.IsFinite(value) || Math.Abs(value) > 1.1f)
                    return false;

                values[valueIndex++] = value;
            }
        }

        double row0Length = Length3(values[0], values[1], values[2]);
        double row1Length = Length3(values[3], values[4], values[5]);
        double row2Length = Length3(values[6], values[7], values[8]);
        double column0Length = Length3(values[0], values[3], values[6]);
        double column1Length = Length3(values[1], values[4], values[7]);
        double column2Length = Length3(values[2], values[5], values[8]);
        double score =
            Math.Abs(row0Length - 1.0) +
            Math.Abs(row1Length - 1.0) +
            Math.Abs(row2Length - 1.0) +
            Math.Abs(column0Length - 1.0) +
            Math.Abs(column1Length - 1.0) +
            Math.Abs(column2Length - 1.0) +
            Math.Abs(Dot3(values[0], values[1], values[2], values[3], values[4], values[5])) +
            Math.Abs(Dot3(values[0], values[1], values[2], values[6], values[7], values[8])) +
            Math.Abs(Dot3(values[3], values[4], values[5], values[6], values[7], values[8])) +
            Math.Abs(Dot3(values[0], values[3], values[6], values[1], values[4], values[7])) +
            Math.Abs(Dot3(values[0], values[3], values[6], values[2], values[5], values[8])) +
            Math.Abs(Dot3(values[1], values[4], values[7], values[2], values[5], values[8]));
        if (score > 0.05)
            return false;

        orientation = new CinematicMeshOrientation(
            values[0],
            values[1],
            values[2],
            values[3],
            values[4],
            values[5],
            values[6],
            values[7],
            values[8],
            HasValue: true);
        return true;
    }

    private static double Length3(float x, float y, float z) =>
        Math.Sqrt((x * x) + (y * y) + (z * z));

    private static double Dot3(float ax, float ay, float az, float bx, float by, float bz) =>
        (ax * bx) + (ay * by) + (az * bz);

    internal static int ReadInt32OrDefault(byte[] bytes, int offset) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))
            : 0;

    internal static int ReadMaterialGraphicAtlasIndexOrDefault(byte[] bytes, int offset)
    {
        int value = ReadInt32OrDefault(bytes, offset);
        // AtlasIndex is serialized as a bitfield with default 0; WiiU cooked
        // data can store the absent/default sentinel as all bits set.
        return value < 0 ? 0 : value;
    }

    internal static TextureAnchor ReadTextureAnchorOrDefault(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return TextureAnchor.MiddleCenter;

        int value = CinematicSceneBoundaryReader.ReadInt32OrDefault(bytes, offset);
        return Enum.IsDefined(typeof(TextureAnchor), value)
            ? (TextureAnchor)value
            : TextureAnchor.MiddleCenter;
    }

    internal static float ReadSingleOrDefault(byte[] bytes, int offset, float defaultValue)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return defaultValue;

        uint raw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    private static bool ReadBool01OrDefault(byte[] bytes, int offset, bool defaultValue)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return defaultValue;

        int value = CinematicSceneBoundaryReader.ReadInt32OrDefault(bytes, offset);
        return value switch
        {
            0 => false,
            1 => true,
            _ => defaultValue
        };
    }

    internal static byte[] ReadFileBytes(JustDanceUbiArtFileSystem fileSystem, CookedFile file)
    {
        using Stream stream = fileSystem.GetFileStream(file);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static bool ContainsBigEndianUInt32(byte[] bytes, uint value)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(needle, value);
        return bytes.AsSpan().IndexOf(needle) >= 0;
    }

    internal static bool IsCleanLegacyAssetPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            normalizedPath.Length > 260 ||
            !normalizedPath.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        return normalizedPath.All(character => !char.IsControl(character));
    }

    internal static bool IsLikelyTexturePath(string normalizedPath) =>
        CinematicNames.IsDynamicPleoTexturePath(normalizedPath) ||
        CinematicSceneBoundaryReader.IsTextureExtension(Path.GetExtension(normalizedPath));

    internal static bool IsLikelyAtlasPath(string normalizedPath)
    {
        string extension = Path.GetExtension(normalizedPath);
        return extension.Equals(".atl", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTextureExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLikelyMaterialPath(string normalizedPath)
    {
        string extension = Path.GetExtension(normalizedPath);
        return extension.Equals(".mat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msh", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsLikelyMeshPath(string normalizedPath)
    {
        string extension = Path.GetExtension(normalizedPath);
        return extension.Equals(".m3d", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(".m3d.ckd", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct CinematicMaterialGraphicFields(
    int AtlasIndex,
    TextureAnchor Anchor,
    float CustomAnchorX,
    float CustomAnchorY);

internal readonly record struct CinematicMesh3DComponentFields(
    CinematicMeshOrientation Orientation,
    bool Force2DRender);
