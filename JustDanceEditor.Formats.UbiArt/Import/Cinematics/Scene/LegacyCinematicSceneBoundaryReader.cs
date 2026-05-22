using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class LegacyCinematicSceneBoundaryReader
{
    internal static bool IsActorBoundaryAt(byte[] bytes, int offset)
    {
        if (offset == bytes.Length)
            return true;

        if (offset < 0 || offset + 4 > bytes.Length)
            return false;

        uint nextActorType = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return LegacyBinaryTypeRegistry.TryResolve(typeof(LegacyCinematicTypedPickableActorBinary), nextActorType, out _);
    }

    internal static void SnapToNearbyActorBoundary(LegacyCinematicBinaryReader reader, int forwardSearchLength)
    {
        if (LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            return;

        int searchEndOffset = Math.Min(reader.Bytes.Length - 4, reader.Offset + forwardSearchLength);
        for (int offset = reader.Offset; offset <= searchEndOffset; offset++)
        {
            if (!LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                continue;

            reader.Offset = offset;
            return;
        }
    }

    internal static void SnapSceneActorListStartToNextBoundary(
        LegacyCinematicBinaryReader reader,
        int depth,
        IReadOnlyList<string> parentPath,
        ILogger logger,
        bool debugScene)
    {
        if (LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            return;

        int payloadOffset = reader.Offset;
        int searchEndOffset = reader.Bytes.Length - 4;
        for (int offset = payloadOffset; offset <= searchEndOffset; offset++)
        {
            if (!LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                continue;

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

    internal static void SnapEmbeddedSceneTailToNextActorBoundary(
        LegacyCinematicBinaryReader reader,
        int depth,
        IReadOnlyList<string> parentPath,
        ILogger logger,
        bool debugScene)
    {
        if (LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            return;

        int tailOffset = reader.Offset;
        int searchEndOffset = reader.Bytes.Length - 4;
        for (int offset = tailOffset; offset <= searchEndOffset; offset++)
        {
            if (!LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
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

    internal static void SkipActorBoundaryZeroTail(LegacyCinematicBinaryReader reader)
    {
        int boundaryOffset = LegacyCinematicSceneBoundaryReader.GetActorBoundaryOffsetAfterZeroTail(reader);
        if (boundaryOffset > reader.Offset)
            reader.Offset = boundaryOffset;
    }

    internal static int GetActorBoundaryOffsetAfterZeroTail(LegacyCinematicBinaryReader reader)
    {
        int offset = reader.Offset;
        while (offset + 16 <= reader.Bytes.Length)
        {
            if (!LegacyCinematicSceneBoundaryReader.IsZeroBlock(reader.Bytes, offset, 12))
                return reader.Offset;

            offset += 12;
            if (LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
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
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + 8, 1),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + 4, 1),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart, 1));

    internal static float ReadComponentAlpha(byte[] bytes, int componentStart) =>
        Math.Clamp(LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, componentStart + 12, 1), 0, 1);

    internal static LegacyCinematicSinusParameters ReadMaterialGraphicSinusParameters(byte[] bytes, int tailOffset)
    {
        if (tailOffset < 0 || tailOffset + LegacyCinematicConstants.MaterialGraphicTailLength > bytes.Length)
            return default;

        return new LegacyCinematicSinusParameters(
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + LegacyCinematicConstants.MaterialGraphicTailSinusAmplitudeXOffset, 0),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + LegacyCinematicConstants.MaterialGraphicTailSinusAmplitudeYOffset, 0),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + LegacyCinematicConstants.MaterialGraphicTailSinusAmplitudeZOffset, 0),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + LegacyCinematicConstants.MaterialGraphicTailSinusSpeedOffset, 1),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + LegacyCinematicConstants.MaterialGraphicTailAngleXOffset, 0),
            LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(bytes, tailOffset + LegacyCinematicConstants.MaterialGraphicTailAngleYOffset, 0));
    }

    internal static int ReadInt32OrDefault(byte[] bytes, int offset) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))
            : 0;

    internal static TextureAnchor ReadTextureAnchorOrDefault(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return TextureAnchor.MiddleCenter;

        int value = LegacyCinematicSceneBoundaryReader.ReadInt32OrDefault(bytes, offset);
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
        LegacyCinematicSceneBoundaryReader.IsTextureExtension(Path.GetExtension(normalizedPath));

    internal static bool IsTextureExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLikelyMaterialPath(string normalizedPath)
    {
        if (normalizedPath.Contains("/materials/", StringComparison.OrdinalIgnoreCase))
            return true;

        string extension = Path.GetExtension(normalizedPath);
        return extension.Equals(".mat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msh", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsLikelyMeshPath(string normalizedPath)
    {
        if (normalizedPath.Contains("/mesh/", StringComparison.OrdinalIgnoreCase))
            return true;

        string extension = Path.GetExtension(normalizedPath);
        return extension.Equals(".m3d", StringComparison.OrdinalIgnoreCase);
    }
}