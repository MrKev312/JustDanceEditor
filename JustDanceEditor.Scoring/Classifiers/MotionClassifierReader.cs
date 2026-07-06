using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Scoring;

internal static class MotionClassifierReader
{
    public static MotionClassifier Read(ReadOnlySpan<byte> data)
    {
        EndianSpanReader reader = CreateReader(data);

        uint endianness = reader.ReadUInt32();
        if (endianness != 1U)
            throw new NotSupportedException("Only motion classifier files with an endianness marker of 1 are supported.");

        uint version = reader.ReadUInt32();
        string moveName = ReadFixedString(ref reader, 64);
        string songName = ReadFixedString(ref reader, 64);
        string measureSetName = ReadFixedString(ref reader, 64);
        float duration = reader.ReadSingle();
        float lowThreshold = reader.ReadSingle();
        float highThreshold = reader.ReadSingle();

        float autoCorrelationThreshold = -1.0f;
        float directionImpactFactor = -1.0f;
        if (version >= 7)
        {
            autoCorrelationThreshold = reader.ReadSingle();
            directionImpactFactor = reader.ReadSingle();
        }

        ulong measureSetBitfield = reader.ReadUInt64();
        uint customizationBitField = reader.ReadUInt32();
        int scoringAlgorithmType = reader.ReadInt32();
        uint energyMeansCount = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        int measureCount = Math.Abs(scoringAlgorithmType);
        int covarianceCount = scoringAlgorithmType > 0
            ? measureCount
            : measureCount * (measureCount + 1) / 2;

        float[] means = ReadSingles(ref reader, measureCount);
        float[] invertedCovariances = ReadSingles(ref reader, covarianceCount);
        float[] energyMeans = ReadSingles(ref reader, checked((int)energyMeansCount));

        return new MotionClassifier(
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
            means,
            invertedCovariances,
            energyMeans,
            version);
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

    private static float[] ReadSingles(ref EndianSpanReader reader, int count)
    {
        float[] result = new float[count];
        for (int i = 0; i < result.Length; i++)
            result[i] = reader.ReadSingle();
        return result;
    }

    private static string ReadFixedString(ref EndianSpanReader reader, int length)
    {
        ReadOnlySpan<byte> bytes = reader.ReadBytes(length);
        int stringLength = bytes.IndexOf((byte)0);
        if (stringLength < 0)
            stringLength = bytes.Length;
        return Encoding.UTF8.GetString(bytes[..stringLength]);
    }

    private ref struct EndianSpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly bool _isBigEndian;
        private int _offset;

        public EndianSpanReader(ReadOnlySpan<byte> data, bool isBigEndian)
        {
            _data = data;
            _isBigEndian = isBigEndian;
            _offset = 0;
        }

        public uint ReadUInt32()
        {
            ReadOnlySpan<byte> bytes = ReadBytes(sizeof(uint));
            return _isBigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
                : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        public int ReadInt32()
        {
            ReadOnlySpan<byte> bytes = ReadBytes(sizeof(int));
            return _isBigEndian
                ? BinaryPrimitives.ReadInt32BigEndian(bytes)
                : BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        public ulong ReadUInt64()
        {
            ReadOnlySpan<byte> bytes = ReadBytes(sizeof(ulong));
            return _isBigEndian
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