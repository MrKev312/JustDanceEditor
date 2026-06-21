using KevInc.UbiArt.Cinematics.Particles;

using System.Buffers.Binary;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicBezierBranchReader
{
    private const uint BezierTreeComponentCrc = 0x3236CF4C;
    private const uint BezierBranchFxComponentCrc = 0x00251BAC;

    internal static bool TryReadFromActorBytes(
        byte[] bytes,
        int actorStartOffset,
        int actorEndOffset,
        out CinematicBezierBranch? branch)
    {
        branch = null;
        int scanEnd = Math.Min(Math.Min(actorEndOffset, bytes.Length), actorStartOffset + 4096);
        for (int offset = actorStartOffset; offset <= scanEnd - 4; offset++)
        {
            if (ReadUInt32OrDefault(bytes, offset) != BezierTreeComponentCrc)
                continue;

            if (TryReadAt(bytes, offset + 4, scanEnd, out branch))
                return true;
        }

        return false;
    }

    private static bool TryReadAt(
        byte[] bytes,
        int offset,
        int endOffset,
        out CinematicBezierBranch? branch)
    {
        branch = null;
        int cursor = offset;
        if (!TryReadInt32(bytes, ref cursor, endOffset, out int nodeCount) ||
            nodeCount is < 2 or > 32)
        {
            return false;
        }

        List<CinematicBezierNode> nodes = new(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            if (!TryReadVector3(bytes, ref cursor, endOffset, out Vector3 position) ||
                !TryReadVector2(bytes, ref cursor, endOffset, out Vector2 tangent) ||
                !TryReadSingle(bytes, ref cursor, endOffset, out float scale))
            {
                return false;
            }

            if (!TryReadInt32(bytes, ref cursor, endOffset, out int tweenMarker))
                return false;
            if (tweenMarker != 0)
                return false;

            nodes.Add(new CinematicBezierNode(position, tangent, scale));
        }

        if (!TryReadInt32(bytes, ref cursor, endOffset, out int subBranchCount) ||
            subBranchCount != 0)
        {
            return false;
        }

        if (!TryReadInt32(bytes, ref cursor, endOffset, out int componentCount) ||
            componentCount is < 0 or > 16)
        {
            return false;
        }

        bool hasBranchFxComponent = false;
        for (int i = 0; i < componentCount; i++)
        {
            if (!TryReadUInt32(bytes, ref cursor, endOffset, out uint componentType))
                return false;
            hasBranchFxComponent |= componentType == BezierBranchFxComponentCrc;
        }

        if (!hasBranchFxComponent)
            return false;

        if (!TryReadInt32(bytes, ref cursor, endOffset, out int _))
            return false;

        branch = new CinematicBezierBranch(nodes);
        return true;
    }

    private static uint ReadUInt32OrDefault(byte[] bytes, int offset) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))
            : 0;

    private static bool TryReadInt32(byte[] bytes, ref int offset, int endOffset, out int value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > endOffset || offset + 4 > bytes.Length)
            return false;

        value = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        return true;
    }

    private static bool TryReadUInt32(byte[] bytes, ref int offset, int endOffset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > endOffset || offset + 4 > bytes.Length)
            return false;

        value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        return true;
    }

    private static bool TryReadSingle(byte[] bytes, ref int offset, int endOffset, out float value)
    {
        value = 0.0f;
        if (!TryReadUInt32(bytes, ref offset, endOffset, out uint raw))
            return false;

        value = BitConverter.Int32BitsToSingle(unchecked((int)raw));
        return true;
    }

    private static bool TryReadVector2(byte[] bytes, ref int offset, int endOffset, out Vector2 value)
    {
        value = Vector2.Zero;
        if (!TryReadSingle(bytes, ref offset, endOffset, out float x) ||
            !TryReadSingle(bytes, ref offset, endOffset, out float y))
        {
            return false;
        }

        value = new Vector2(x, y);
        return true;
    }

    private static bool TryReadVector3(byte[] bytes, ref int offset, int endOffset, out Vector3 value)
    {
        value = Vector3.Zero;
        if (!TryReadSingle(bytes, ref offset, endOffset, out float x) ||
            !TryReadSingle(bytes, ref offset, endOffset, out float y) ||
            !TryReadSingle(bytes, ref offset, endOffset, out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }
}
