using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Timeline;

using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal sealed class CinematicBinaryReader(byte[] bytes, int offset = 0) : ILegacyBinaryReader
{
    private const uint BezierCurveFloatEmptyCrc = 0x165181F0;
    private const uint BezierCurveFloatConstantCrc = 0xB7914191;
    private const uint BezierCurveFloatLinearCrc = 0x4DE6D871;
    private const uint BezierCurveFloatMultiCrc = 0xE2BC4FB2;
    private const uint NullFactoryObjectCrc = 0xFFFFFFFF;

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
        if (length == -1)
            return string.Empty;

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
        SkipUInt32();
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

        if (tableSize is not 0x30 and not 0x24)
            throw new InvalidDataException($"Invalid legacy actor target descriptor at 0x{Offset - 12:X}.");

        if (segmentCount == 0)
        {
            if (segmentHeaderSize <= 0)
            {
                if (Remaining >= 4)
                    Skip(4);

                return new ActorTargetPath([]);
            }

            string spawnedActorName = ReadStringPayload(segmentHeaderSize);
            if (Remaining >= 4)
                Skip(4);

            return new ActorTargetPath([spawnedActorName]);
        }

        if (tableSize == 0x24 &&
            segmentHeaderSize == 0x10 &&
            LooksLikeCompactSerializedObjectPath(segmentCount))
        {
            return ReadCompactSerializedObjectPathTarget(segmentCount, segmentHeaderSize);
        }

        if (tableSize == 0x30 &&
            segmentHeaderSize == 0x18 &&
            LooksLikePaddedCompactSerializedObjectPath(segmentCount))
        {
            return ReadPaddedCompactSerializedObjectPathTarget(segmentCount, segmentHeaderSize);
        }

        int unknown0 = ReadInt32();
        int unknown1 = ReadInt32();
        int firstSegmentLengthOrHeaderSize = ReadInt32();

        if (segmentHeaderSize is not 0x18 and not 0x10 || unknown0 != 0 || unknown1 != 1)
            throw new InvalidDataException($"Invalid legacy actor target descriptor at 0x{Offset - 24:X}.");

        if (segmentCount is <= 0 or > 32)
            throw new InvalidDataException($"Invalid legacy actor target segment count {segmentCount} at 0x{Offset - 20:X}.");

        int? firstSegmentLength = firstSegmentLengthOrHeaderSize == segmentHeaderSize
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

    private bool LooksLikeCompactSerializedObjectPath(int levelCount)
    {
        if (levelCount is <= 0 or > 32 || Remaining < 12)
            return false;

        int length = PeekInt32();
        return length is > 0 and <= 4096 && Offset + 4 + length + 4 <= Bytes.Length;
    }

    private bool LooksLikePaddedCompactSerializedObjectPath(int levelCount)
    {
        if (levelCount is <= 0 or > 32 || Remaining < 12)
            return false;

        int length = PeekInt32();
        return length is > 0 and <= 4096 && Offset + 4 + length + 4 <= Bytes.Length;
    }

    private ActorTargetPath ReadCompactSerializedObjectPathTarget(int levelCount, int segmentHeaderSize)
    {
        List<string> segments = new(levelCount + 1);
        for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
        {
            string levelName = ReadString();
            SkipInt32();
            if (!string.IsNullOrWhiteSpace(levelName))
                segments.Add(levelName);

            if (levelIndex < levelCount - 1)
                TryConsumeInterleavedTargetSegmentHeader(segmentHeaderSize);
        }

        string objectName = ReadString();
        if (!string.IsNullOrWhiteSpace(objectName))
            segments.Add(objectName);

        if (Remaining >= 4)
            SkipInt32();

        return new ActorTargetPath([.. segments]);
    }

    private bool TryConsumeInterleavedTargetSegmentHeader(int segmentHeaderSize)
    {
        if (Remaining < 8 || PeekInt32() != segmentHeaderSize)
            return false;

        int lengthOffset = Offset + sizeof(int);
        int nextStringLength = BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(lengthOffset, sizeof(int)));
        if (nextStringLength <= 0 ||
            nextStringLength > 4096 ||
            lengthOffset + sizeof(int) + nextStringLength > Bytes.Length)
        {
            return false;
        }

        Skip(sizeof(int));
        return true;
    }

    private ActorTargetPath ReadPaddedCompactSerializedObjectPathTarget(int levelCount, int segmentHeaderSize)
    {
        List<string> segments = new(levelCount + 1);
        for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
        {
            string levelName = ReadString();
            if (!string.IsNullOrWhiteSpace(levelName))
                segments.Add(levelName);

            SkipInt32();

            if (levelIndex < levelCount - 1)
            {
                int nextHeaderSize = ReadInt32();
                if (nextHeaderSize != segmentHeaderSize)
                    throw new InvalidDataException($"Invalid legacy actor target segment header {nextHeaderSize} at 0x{Offset - 4:X}.");
            }
        }

        string objectName = ReadString();
        if (!string.IsNullOrWhiteSpace(objectName))
            segments.Add(objectName);

        if (Remaining >= 4)
            SkipInt32();

        return new ActorTargetPath([.. segments]);
    }

    public IReadOnlyList<CinematicCurve?> ReadCurveBlocks(int expectedCount)
    {
        List<CinematicCurve?> curves = new(expectedCount);
        while (curves.Count < expectedCount && Remaining >= 4)
            curves.Add(ReadCurveBlock());

        return curves;
    }

    private CinematicCurve? ReadCurveBlock()
    {
        int startOffset = Offset;
        if (TryReadFactoryCurveObject(out CinematicCurve? factoryCurve))
            return factoryCurve;

        int marker = ReadInt32();
        if (marker == 0)
            return null;

        return ReadBezierCurveBody(marker, startOffset);
    }

    private bool TryReadFactoryCurveObject(out CinematicCurve? curve)
    {
        curve = null;
        if (Remaining < 8 || PeekInt32() != 4)
            return false;

        uint typeId = BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(Offset + 4, 4));
        switch (typeId)
        {
            case NullFactoryObjectCrc:
            case BezierCurveFloatEmptyCrc:
                Skip(8);
                return true;
            case BezierCurveFloatConstantCrc:
                Skip(8);
                curve = ReadConstantCurveBody();
                return true;
            case BezierCurveFloatLinearCrc:
                Skip(8);
                curve = ReadLinearCurveBody();
                return true;
            case BezierCurveFloatMultiCrc:
                Skip(8);
                curve = ReadBezierCurveBody(ReadInt32(), Offset - 4);
                return true;
            default:
                return false;
        }
    }

    private CinematicCurve? ReadConstantCurveBody()
    {
        int bodySize = ReadInt32();
        if (bodySize != 0x08)
            throw new InvalidDataException($"Invalid legacy constant curve size {bodySize} at 0x{Offset - 4:X}.");

        float value = ReadSingle();
        return new CinematicCurve([new CinematicKeyframe(0, value, 0, value, 0, value)]);
    }

    private CinematicCurve ReadLinearCurveBody()
    {
        int bodySize = ReadInt32();
        if (bodySize != 0x24)
            throw new InvalidDataException($"Invalid legacy linear curve size {bodySize} at 0x{Offset - 4:X}.");

        float leftTime = ReadSingle();
        float leftValue = ReadSingle();
        float leftOutTime = ReadSingle();
        float leftOutValue = ReadSingle();
        float rightTime = ReadSingle();
        float rightValue = ReadSingle();
        float rightInTime = ReadSingle();
        float rightInValue = ReadSingle();

        return new CinematicCurve(
            [
                new CinematicKeyframe(leftTime, leftValue, leftTime, leftValue, leftOutTime, leftOutValue),
                new CinematicKeyframe(rightTime, rightValue, rightInTime, rightInValue, rightTime, rightValue)
            ]);
    }

    private CinematicCurve? ReadBezierCurveBody(int marker, int markerOffset)
    {
        if (marker != 0x14)
            throw new InvalidDataException($"Invalid legacy curve marker 0x{marker:X8} at 0x{markerOffset:X}.");

        int keyCount = ReadInt32();
        if (keyCount == 0)
            return null;

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

        return new CinematicCurve([.. keyframes.OrderBy(item => item.Time)]);
    }

    internal static bool TryReadCurveFactoryHeader(byte[] bytes, int offset, out uint typeId)
    {
        typeId = 0;
        if (offset < 0 || offset + 8 > bytes.Length)
            return false;

        int factoryHeaderSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
        if (factoryHeaderSize != 4)
            return false;

        typeId = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 4, 4));
        return typeId is NullFactoryObjectCrc
            or BezierCurveFloatEmptyCrc
            or BezierCurveFloatConstantCrc
            or BezierCurveFloatLinearCrc
            or BezierCurveFloatMultiCrc;
    }

    internal static string GetCurveFactoryTypeName(uint typeId)
    {
        return typeId switch
        {
            NullFactoryObjectCrc => "Null",
            BezierCurveFloatEmptyCrc => "BezierCurveFloatEmpty",
            BezierCurveFloatConstantCrc => "BezierCurveFloatConstant",
            BezierCurveFloatLinearCrc => "BezierCurveFloatLinear",
            BezierCurveFloatMultiCrc => "BezierCurveFloatMulti",
            _ => $"0x{typeId:X8}"
        };
    }

    public void Skip(int count)
    {
        if (count < 0 || Offset + count > Bytes.Length)
            throw new InvalidDataException($"Cannot skip {count} byte(s) at 0x{Offset:X}.");

        Offset += count;
    }

    public void SkipByte() => Skip(1);

    public void SkipInt32() => Skip(sizeof(int));

    public void SkipUInt32() => Skip(sizeof(uint));

    public void SkipSingle() => Skip(sizeof(float));

    public void SkipString()
    {
        int length = ReadInt32();
        if (length == -1)
            return;

        if (length < 0 || length > 4096 || Offset + length > Bytes.Length)
            throw new InvalidDataException($"Invalid legacy string length {length} at 0x{Offset - 4:X}.");

        Offset += length;
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