using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal sealed class LegacyCinematicBinaryReader(byte[] bytes, int offset = 0) : ILegacyBinaryReader
{
    public byte[] Bytes { get; } = bytes;
    public int Offset { get; set; } = offset;
    public int Remaining => Bytes.Length - Offset;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return Bytes[Offset++];
    }

    public int PeekInt32()
    {
        EnsureAvailable(4);
        return BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(Offset, 4));
    }

    public short ReadInt16()
    {
        EnsureAvailable(2);
        short value = BinaryPrimitives.ReadInt16BigEndian(Bytes.AsSpan(Offset, 2));
        Offset += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(Offset, 2));
        Offset += 2;
        return value;
    }

    public uint PeekUInt32()
    {
        EnsureAvailable(4);
        return BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(Offset, 4));
    }

    public int ReadInt32()
    {
        int value = PeekInt32();
        Offset += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        uint value = PeekUInt32();
        Offset += 4;
        return value;
    }

    public long ReadInt64()
    {
        EnsureAvailable(8);
        long value = BinaryPrimitives.ReadInt64BigEndian(Bytes.AsSpan(Offset, 8));
        Offset += 8;
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(8);
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(Bytes.AsSpan(Offset, 8));
        Offset += 8;
        return value;
    }

    public float ReadSingle()
    {
        uint raw = ReadUInt32();
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    public double ReadDouble()
    {
        ulong raw = ReadUInt64();
        return BitConverter.Int64BitsToDouble(unchecked((long)raw));
    }

    public string ReadString()
    {
        int length = ReadInt32();
        if (length < 0 || length > 4096 || Offset + length > Bytes.Length)
            throw new InvalidDataException($"Invalid legacy string length {length} at 0x{Offset - 4:X}.");

        string value = Encoding.UTF8.GetString(Bytes, Offset, length).TrimEnd('\0');
        Offset += length;
        return value;
    }

    public byte[] ReadBytes(int length)
    {
        if (length < 0 || Offset + length > Bytes.Length)
            throw new InvalidDataException($"Cannot read {length} byte(s) at 0x{Offset:X}.");

        byte[] value = Bytes.AsSpan(Offset, length).ToArray();
        Offset += length;
        return value;
    }

    public string ReadPath()
    {
        string first = ReadString();
        string second = ReadString();
        _ = ReadUInt32();
        return LegacyUbiArtPathOrder.Resolve(first, second);
    }

    public IReadOnlyList<ActorTargetPath> ReadTargetList()
    {
        int targetCount = ReadInt32();
        if (targetCount is < 0 or > 1024)
            throw new InvalidDataException($"Invalid legacy actor target count {targetCount} at 0x{Offset - 4:X}.");

        List<ActorTargetPath> targets = new(targetCount);
        for (int i = 0; i < targetCount; i++)
            targets.Add(ReadTargetDescriptor());

        return targets;
    }

    public ActorTargetPath ReadTargetDescriptor()
    {
        int tableSize = ReadInt32();
        int segmentCount = ReadInt32();
        int segmentHeaderSize = ReadInt32();
        int unknown0 = ReadInt32();
        int unknown1 = ReadInt32();
        int firstSegmentLengthOrHeaderSize = ReadInt32();

        if (tableSize != 0x30 || segmentHeaderSize != 0x18 || unknown0 != 0 || unknown1 != 1)
            throw new InvalidDataException($"Invalid legacy actor target descriptor at 0x{Offset - 24:X}.");

        if (segmentCount is <= 0 or > 32)
            throw new InvalidDataException($"Invalid legacy actor target segment count {segmentCount} at 0x{Offset - 20:X}.");

        int? firstSegmentLength = firstSegmentLengthOrHeaderSize == 0x18
            ? null
            : firstSegmentLengthOrHeaderSize;
        if (firstSegmentLength != null && segmentCount != 1)
            throw new InvalidDataException($"Invalid legacy actor target segment header {firstSegmentLengthOrHeaderSize} at 0x{Offset - 4:X}.");

        string[] segments = new string[segmentCount];
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            segments[segmentIndex] = segmentIndex == 0 && firstSegmentLength != null
                ? ReadStringPayload(firstSegmentLength.Value)
                : ReadString();
            Skip(segmentIndex < segmentCount - 2 ? 8 : 4);
        }

        return new ActorTargetPath(segments);
    }

    public IReadOnlyList<CinematicCurve?> ReadCurveBlocks(int expectedCount)
    {
        List<CinematicCurve?> curves = new(expectedCount);
        while (curves.Count < expectedCount && Remaining >= 4)
        {
            int marker = ReadInt32();
            if (marker == 0)
            {
                curves.Add(null);
                continue;
            }

            if (marker != 0x14)
                throw new InvalidDataException($"Invalid legacy curve marker 0x{marker:X8} at 0x{Offset - 4:X}.");

            int keyCount = ReadInt32();
            if (keyCount == 0)
            {
                curves.Add(null);
                continue;
            }

            if (keyCount is < 0 or > 64)
                throw new InvalidDataException($"Invalid legacy curve key count {keyCount} at 0x{Offset - 8:X}.");

            List<CinematicKeyframe> keyframes = new(keyCount);
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                int recordSize = ReadInt32();
                if (recordSize != 0x18)
                    throw new InvalidDataException($"Invalid legacy curve keyframe record size {recordSize} at 0x{Offset - 4:X}.");

                keyframes.Add(new CinematicKeyframe(
                    ReadSingle(),
                    ReadSingle(),
                    ReadSingle(),
                    ReadSingle(),
                    ReadSingle(),
                    ReadSingle()));
            }

            curves.Add(new CinematicCurve([.. keyframes.OrderBy(item => item.Time)]));
        }

        return curves;
    }

    public void Skip(int count)
    {
        if (count < 0 || Offset + count > Bytes.Length)
            throw new InvalidDataException($"Cannot skip {count} byte(s) at 0x{Offset:X}.");

        Offset += count;
    }

    private void EnsureAvailable(int count)
    {
        if (Offset < 0 || Offset + count > Bytes.Length)
            throw new InvalidDataException($"Cannot read {count} byte(s) at 0x{Offset:X}.");
    }

    private string ReadStringPayload(int length)
    {
        if (length < 0 || length > 4096 || Offset + length > Bytes.Length)
            throw new InvalidDataException($"Invalid legacy string length {length} at 0x{Offset:X}.");

        string value = Encoding.UTF8.GetString(Bytes, Offset, length).TrimEnd('\0');
        Offset += length;
        return value;
    }
}