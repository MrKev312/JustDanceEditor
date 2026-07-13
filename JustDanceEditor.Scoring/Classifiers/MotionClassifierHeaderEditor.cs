using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Scoring;

public sealed record MotionClassifierHeader(
    string SongName,
    string MoveName,
    string MeasureSetName,
    float Duration,
    float LowThreshold,
    float HighThreshold,
    float AutoCorrelationThreshold,
    float DirectionImpactFactor,
    ulong MeasureSetBitfield,
    uint CustomizationBitField,
    int ScoringAlgorithmType,
    uint EnergyMeansCount,
    MotionClassifierFormatVersion FormatVersion,
    bool IsBigEndian);

public sealed record MotionClassifierHeaderUpdate
{
    public float? LowThreshold { get; init; }
    public float? HighThreshold { get; init; }
    public float? AutoCorrelationThreshold { get; init; }
    public float? DirectionImpactFactor { get; init; }
    public uint? CustomizationBitField { get; init; }
}

public static class MotionClassifierHeaderEditor
{
    private const int FixedStringLength = 64;
    private const int LowThresholdOffset = 8 + (FixedStringLength * 3) + sizeof(float);
    private const int HighThresholdOffset = LowThresholdOffset + sizeof(float);
    private const int AutoCorrelationThresholdOffset = HighThresholdOffset + sizeof(float);
    private const int DirectionImpactFactorOffset = AutoCorrelationThresholdOffset + sizeof(float);
    private const int VersionSevenBitfieldOffset = DirectionImpactFactorOffset + sizeof(float);
    private const int VersionSixBitfieldOffset = AutoCorrelationThresholdOffset;

    public static MotionClassifierHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        EndianSpanReader reader = CreateReader(data);

        uint endianness = reader.ReadUInt32();
        if (endianness != 1U)
            throw new NotSupportedException("Only motion classifier files with an endianness marker of 1 are supported.");

        MotionClassifierFormatVersion version = MotionClassifierFormatRules.Parse(reader.ReadUInt32());
        string moveName = ReadFixedString(ref reader, FixedStringLength);
        string songName = ReadFixedString(ref reader, FixedStringLength);
        string measureSetName = ReadFixedString(ref reader, FixedStringLength);
        float duration = reader.ReadSingle();
        float lowThreshold = reader.ReadSingle();
        float highThreshold = reader.ReadSingle();

        float autoCorrelationThreshold = -1.0f;
        float directionImpactFactor = -1.0f;
        if (MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(version))
        {
            autoCorrelationThreshold = reader.ReadSingle();
            directionImpactFactor = reader.ReadSingle();
        }

        ulong measureSetBitfield = reader.ReadUInt64();
        uint customizationBitField = MotionClassifierFormatRules.HasCustomizationBitField(version) ? reader.ReadUInt32() : 0U;
        int scoringAlgorithmType = reader.ReadInt32();
        uint energyMeansCount = reader.ReadUInt32();

        return new MotionClassifierHeader(
            songName,
            moveName,
            measureSetName,
            duration,
            lowThreshold,
            highThreshold,
            autoCorrelationThreshold,
            directionImpactFactor,
            measureSetBitfield,
            customizationBitField,
            scoringAlgorithmType,
            energyMeansCount,
            version,
            reader.IsBigEndian);
    }

    public static byte[] UpdateHeader(ReadOnlySpan<byte> source, MotionClassifierHeaderUpdate update)
    {
        byte[] result = source.ToArray();
        MotionClassifierHeader header = ReadHeader(result);

        if (update.LowThreshold.HasValue)
            WriteSingle(result, LowThresholdOffset, update.LowThreshold.Value, header.IsBigEndian);
        if (update.HighThreshold.HasValue)
            WriteSingle(result, HighThresholdOffset, update.HighThreshold.Value, header.IsBigEndian);

        if (MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(header.FormatVersion))
        {
            if (update.AutoCorrelationThreshold.HasValue)
                WriteSingle(result, AutoCorrelationThresholdOffset, update.AutoCorrelationThreshold.Value, header.IsBigEndian);
            if (update.DirectionImpactFactor.HasValue)
                WriteSingle(result, DirectionImpactFactorOffset, update.DirectionImpactFactor.Value, header.IsBigEndian);
        }

        if (MotionClassifierFormatRules.HasCustomizationBitField(header.FormatVersion) && update.CustomizationBitField.HasValue)
        {
            int customizationOffset = MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(header.FormatVersion)
                ? VersionSevenBitfieldOffset + sizeof(ulong)
                : VersionSixBitfieldOffset + sizeof(ulong);
            WriteUInt32(result, customizationOffset, update.CustomizationBitField.Value, header.IsBigEndian);
        }

        return result;
    }

    private static EndianSpanReader CreateReader(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
            throw new InvalidDataException("Motion classifier data is too short.");

        if (BinaryPrimitives.ReadUInt32LittleEndian(data[..sizeof(uint)]) == 1U)
            return new EndianSpanReader(data, isBigEndian: false);

        if (BinaryPrimitives.ReadUInt32BigEndian(data[..sizeof(uint)]) == 1U)
            return new EndianSpanReader(data, isBigEndian: true);

        throw new NotSupportedException("Only motion classifier files with a recognized endianness marker are supported.");
    }

    private static string ReadFixedString(ref EndianSpanReader reader, int length)
    {
        ReadOnlySpan<byte> bytes = reader.ReadBytes(length);
        int stringLength = bytes.IndexOf((byte)0);
        if (stringLength < 0)
            stringLength = bytes.Length;
        return Encoding.UTF8.GetString(bytes[..stringLength]);
    }

    private static void WriteSingle(byte[] data, int offset, float value, bool isBigEndian)
        => WriteInt32(data, offset, BitConverter.SingleToInt32Bits(value), isBigEndian);

    private static void WriteUInt32(byte[] data, int offset, uint value, bool isBigEndian)
    {
        if (offset < 0 || offset + sizeof(uint) > data.Length)
            throw new EndOfStreamException("Unexpected end of motion classifier data.");

        if (isBigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, sizeof(uint)), value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteInt32(byte[] data, int offset, int value, bool isBigEndian)
    {
        if (offset < 0 || offset + sizeof(int) > data.Length)
            throw new EndOfStreamException("Unexpected end of motion classifier data.");

        if (isBigEndian)
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, sizeof(int)), value);
        else
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
    }

    private ref struct EndianSpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public EndianSpanReader(ReadOnlySpan<byte> data, bool isBigEndian)
        {
            _data = data;
            IsBigEndian = isBigEndian;
            _offset = 0;
        }

        public bool IsBigEndian { get; }

        public uint ReadUInt32()
        {
            ReadOnlySpan<byte> bytes = ReadBytes(sizeof(uint));
            return IsBigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
                : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        public int ReadInt32()
        {
            ReadOnlySpan<byte> bytes = ReadBytes(sizeof(int));
            return IsBigEndian
                ? BinaryPrimitives.ReadInt32BigEndian(bytes)
                : BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        public ulong ReadUInt64()
        {
            ReadOnlySpan<byte> bytes = ReadBytes(sizeof(ulong));
            return IsBigEndian
                ? BinaryPrimitives.ReadUInt64BigEndian(bytes)
                : BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }

        public float ReadSingle()
            => BitConverter.Int32BitsToSingle(ReadInt32());

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || _offset + count > _data.Length)
                throw new EndOfStreamException("Unexpected end of motion classifier data.");

            ReadOnlySpan<byte> result = _data.Slice(_offset, count);
            _offset += count;
            return result;
        }
    }
}