using JustDanceEditor.Formats.JDI.Recordings;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Recordings;

/// <summary>
/// Reads and writes the big-endian REC v4 motion-capture format used by the
/// original Just Dance recording and classifier tools.
/// </summary>
internal static partial class RecMotionRecordingCodec
{
    private const int FormatNameLength = 8;
    private const int MapNameLength = 64;
    private const int FieldNameLength = 8;
    private const uint MaximumListLength = 256;

    private const string ListField = "LIST____";
    private const string TimeField = "TIME_MS_";
    private const string InputDescriptionField = "INPUTDSC";
    private const string WiiFormat = "WII_ACCQ";
    private const string ModernFormat = "NX_ACCQD";

    public static IReadOnlyList<MotionRecordingDocument> Read(
        Stream stream,
        int firstCoachId,
        string? sourceFileName = null,
        string? songId = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The REC stream must be readable.", nameof(stream));
        if (firstCoachId < 0)
            throw new ArgumentOutOfRangeException(nameof(firstCoachId));
        if (!stream.CanSeek)
        {
            using MemoryStream buffered = new();
            stream.CopyTo(buffered);
            buffered.Position = 0;
            return Read(buffered, firstCoachId, sourceFileName, songId);
        }

        ParsedRec parsed = Parse(stream);
        DateTimeOffset startedAt = InferStartedAtUtc(sourceFileName) ?? DateTimeOffset.UtcNow;
        List<MotionRecordingDocument> documents = [];

        int coachOffset = 0;
        foreach ((uint padId, List<RecordedMotionSample> samples) in parsed.SamplesByPad.OrderBy(static pair => pair.Key))
        {
            if (samples.Count == 0)
                continue;

            samples.Sort(static (left, right) => left.MapTime.CompareTo(right.MapTime));
            MotionRecordingDocument document = new()
            {
                CoachId = firstCoachId + coachOffset++,
                DeviceId = $"imported-motion-controller-{padId}",
                DeviceName = $"Imported motion controller {padId}",
                SongId = songId,
                MapName = parsed.MapName,
                StartedAtUtc = startedAt,
                TimelineStartSeconds = samples[0].MapTime,
                TimelineEndSeconds = samples[^1].MapTime,
                Samples = samples
            };

            documents.Add(document);
        }

        if (documents.Count == 0)
            throw new InvalidDataException("The REC file does not contain motion samples.");

        return documents;
    }

    public static void Write(
        Stream stream,
        MotionRecordingDocument recording,
        RecMotionFormat format,
        string? mapName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(recording);
        if (!stream.CanWrite)
            throw new ArgumentException("The REC stream must be writable.", nameof(stream));

        bool isWii = format == RecMotionFormat.Wii;
        string formatName = isWii ? WiiFormat : ModernFormat;
        string accelField = isWii ? "WRM_AR1_" : "GEM_AR1_";
        string gyroField = isWii ? "WRM_GR1A" : "GEM_GR1A";

        WriteHeader(stream, formatName, mapName ?? recording.MapName ?? string.Empty, accelField, gyroField);

        foreach (IGrouping<int, RecordedMotionSample> group in recording.Samples
                     .OrderBy(static sample => sample.MapTime)
                     .GroupBy(static sample => checked((int)Math.Round(sample.MapTime * 1000.0, MidpointRounding.AwayFromZero))))
        {
            RecordedMotionSample[] groupedSamples = [.. group];
            for (int start = 0; start < groupedSamples.Length; start += checked((int)MaximumListLength))
            {
                List<RecordedMotionSample> samples =
                [
                    .. groupedSamples.Skip(start).Take(checked((int)MaximumListLength))
                ];
                if (isWii)
                    samples.Reverse();

                WriteUInt32(stream, unchecked((uint)group.Key));
                WriteUInt32(stream, checked((uint)samples.Count));
                foreach (RecordedMotionSample sample in samples)
                {
                    WriteSingle(stream, sample.AccX);
                    WriteSingle(stream, sample.AccY);
                    WriteSingle(stream, sample.AccZ);
                }

                WriteUInt32(stream, checked((uint)samples.Count));
                foreach (RecordedMotionSample sample in samples)
                {
                    WriteSingle(stream, sample.GyroX);
                    WriteSingle(stream, sample.GyroY);
                    WriteSingle(stream, sample.GyroZ);
                }
            }
        }
    }

    public static int? InferCoachId(string? fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
        Match match = MovesPrefixRegex().Match(stem);
        if (!match.Success)
            return null;
        if (string.IsNullOrEmpty(match.Groups[1].Value))
            return 0;
        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int layer) && layer > 0
            ? layer - 1
            : null;
    }

    private static ParsedRec Parse(Stream stream)
    {
        uint headerSize = ReadUInt32(stream);
        if (headerSize < FormatNameLength + sizeof(uint) + MapNameLength + sizeof(uint) + sizeof(uint))
            throw new InvalidDataException($"Invalid REC header size {headerSize}.");

        string formatName = ReadFixedString(stream, FormatNameLength);
        uint version = ReadUInt32(stream);
        string mapName = ReadFixedString(stream, MapNameLength);
        uint headerFieldCount = ReadUInt32(stream);
        if (headerFieldCount == 0 || headerFieldCount > 128)
            throw new InvalidDataException($"Invalid REC field count {headerFieldCount}.");

        List<RecField> fields = [];
        bool nextIsList = false;
        for (uint i = 0; i < headerFieldCount; i++)
        {
            string name = ReadFixedString(stream, FieldNameLength, trimNulls: false);
            uint size = ReadUInt32(stream);
            if (string.Equals(name, ListField, StringComparison.Ordinal))
            {
                nextIsList = true;
                continue;
            }

            fields.Add(new RecField(name.TrimEnd('\0'), size, nextIsList, GetFieldKind(name)));
            nextIsList = false;
        }

        uint reservedSize = ReadUInt32(stream);
        SkipExactly(stream, reservedSize);

        long expectedBodyOffset = checked(headerSize + sizeof(uint));
        if (stream.Position != expectedBodyOffset)
            throw new InvalidDataException($"REC header ended at 0x{stream.Position:X}, expected 0x{expectedBodyOffset:X}.");

        Dictionary<uint, List<RecordedMotionSample>> samplesByPad = [];
        bool isWii = string.Equals(formatName, WiiFormat, StringComparison.Ordinal);
        while (stream.Position < stream.Length)
            ReadChunk(stream, fields, isWii, samplesByPad);

        return new ParsedRec(formatName, version, mapName, samplesByPad);
    }

    private static void ReadChunk(
        Stream stream,
        IReadOnlyList<RecField> fields,
        bool isWii,
        Dictionary<uint, List<RecordedMotionSample>> samplesByPad)
    {
        double mapTime = double.NaN;
        List<float> acceleration = [];
        List<float> gyro = [];
        List<InputDescription> inputs = [];

        foreach (RecField field in fields)
        {
            uint count = field.IsList ? ReadUInt32(stream) : 1;
            if (count > MaximumListLength)
                throw new InvalidDataException($"REC field '{field.Name}' contains {count} values; the maximum is {MaximumListLength}.");

            switch (field.Kind)
            {
                case RecFieldKind.Time:
                    if (count != 1)
                        throw new InvalidDataException("REC timestamps cannot be lists.");
                    mapTime = unchecked((int)ReadUInt32(stream)) * 0.001;
                    break;

                case RecFieldKind.Acceleration:
                    ReadVectors(stream, count, acceleration);
                    break;

                case RecFieldKind.Gyro:
                    ReadVectors(stream, count, gyro);
                    break;

                case RecFieldKind.InputDescription:
                    for (uint i = 0; i < count; i++)
                    {
                        inputs.Add(new InputDescription(ReadUInt32(stream), ReadUInt32(stream), ReadUInt32(stream)));
                        _ = ReadUInt32(stream);
                    }
                    break;

                case RecFieldKind.Float:
                    for (uint i = 0; i < count; i++)
                        _ = ReadSingle(stream);
                    break;

                default:
                    SkipExactly(stream, checked(field.Size * count));
                    break;
            }
        }

        if (double.IsNaN(mapTime))
            throw new InvalidDataException("REC chunk is missing TIME_MS_.");

        int availableVectors = Math.Max(acceleration.Count, gyro.Count) / 3;
        if (inputs.Count == 0)
            inputs.Add(new InputDescription(0, 0, checked((uint)availableVectors)));

        foreach (InputDescription input in inputs)
        {
            List<float> padAcceleration = SliceVectors(acceleration, input.FirstVectorIndex, input.VectorCount);
            List<float> padGyro = SliceVectors(gyro, input.FirstVectorIndex, input.VectorCount);
            if (isWii)
            {
                ReverseVectors(padAcceleration);
                ReverseVectors(padGyro);
            }

            int vectorCount = Math.Max(padAcceleration.Count, padGyro.Count) / 3;
            if (vectorCount == 0)
                continue;

            if (!samplesByPad.TryGetValue(input.PadId, out List<RecordedMotionSample>? destination))
            {
                destination = [];
                samplesByPad.Add(input.PadId, destination);
            }

            for (int vector = 0; vector < vectorCount; vector++)
            {
                int offset = vector * 3;
                destination.Add(new RecordedMotionSample
                {
                    MapTime = mapTime,
                    AccX = GetComponent(padAcceleration, offset),
                    AccY = GetComponent(padAcceleration, offset + 1),
                    AccZ = GetComponent(padAcceleration, offset + 2),
                    GyroX = GetComponent(padGyro, offset),
                    GyroY = GetComponent(padGyro, offset + 1),
                    GyroZ = GetComponent(padGyro, offset + 2)
                });
            }
        }
    }

    private static void WriteHeader(Stream stream, string formatName, string mapName, string accelField, string gyroField)
    {
        const uint serializedFieldCount = 5;
        const uint headerSize = FormatNameLength + sizeof(uint) + MapNameLength + sizeof(uint)
                                + (serializedFieldCount * (FieldNameLength + sizeof(uint))) + sizeof(uint);

        WriteUInt32(stream, headerSize);
        WriteFixedString(stream, formatName, FormatNameLength);
        WriteUInt32(stream, 4);
        WriteFixedString(stream, mapName, MapNameLength);
        WriteUInt32(stream, serializedFieldCount);
        WriteField(stream, TimeField, sizeof(uint));
        WriteField(stream, ListField, 0);
        WriteField(stream, accelField, sizeof(float) * 3);
        WriteField(stream, ListField, 0);
        WriteField(stream, gyroField, sizeof(float) * 3);
        WriteUInt32(stream, 0);
    }

    private static void WriteField(Stream stream, string name, uint size)
    {
        WriteFixedString(stream, name, FieldNameLength);
        WriteUInt32(stream, size);
    }

    private static RecFieldKind GetFieldKind(string paddedName)
    {
        string name = paddedName.TrimEnd('\0');
        if (string.Equals(name, TimeField, StringComparison.Ordinal))
            return RecFieldKind.Time;
        if (string.Equals(name, InputDescriptionField, StringComparison.Ordinal))
            return RecFieldKind.InputDescription;
        if (name.StartsWith("WRM_AR", StringComparison.Ordinal) || name.StartsWith("GEM_AR", StringComparison.Ordinal))
            return RecFieldKind.Acceleration;
        if (name.StartsWith("WRM_GR", StringComparison.Ordinal) || name.StartsWith("GEM_GR", StringComparison.Ordinal))
            return RecFieldKind.Gyro;
        if (string.Equals(name, "QUALITY", StringComparison.Ordinal))
            return RecFieldKind.Float;
        return RecFieldKind.Unknown;
    }

    private static void ReadVectors(Stream stream, uint count, List<float> destination)
    {
        for (uint i = 0; i < count; i++)
        {
            destination.Add(ReadSingle(stream));
            destination.Add(ReadSingle(stream));
            destination.Add(ReadSingle(stream));
        }
    }

    private static List<float> SliceVectors(List<float> source, uint firstVector, uint vectorCount)
    {
        int start = checked((int)firstVector * 3);
        int count = checked((int)vectorCount * 3);
        if (start >= source.Count)
            return [];
        count = Math.Min(count, source.Count - start);
        return source.GetRange(start, count);
    }

    private static void ReverseVectors(List<float> values)
    {
        for (int left = 0, right = values.Count - 3; left < right; left += 3, right -= 3)
        {
            for (int component = 0; component < 3; component++)
                (values[left + component], values[right + component]) = (values[right + component], values[left + component]);
        }
    }

    private static float GetComponent(List<float> values, int index) => index < values.Count ? values[index] : 0;

    private static DateTimeOffset? InferStartedAtUtc(string? fileName)
    {
        Match match = TimestampRegex().Match(Path.GetFileNameWithoutExtension(fileName) ?? string.Empty);
        if (!match.Success)
            return null;

        string value = match.Groups[1].Value;
        string format = value.Count(static character => character == '-') == 4
            ? "yyyy-MM-dd_HH-mm-ss"
            : "yyyy-MM-dd_HH-mm";
        return DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime timestamp)
            ? new DateTimeOffset(timestamp).ToUniversalTime()
            : null;
    }

    private static string ReadFixedString(Stream stream, int length, bool trimNulls = true)
    {
        byte[] bytes = new byte[length];
        ReadExactly(stream, bytes);
        string value = Encoding.ASCII.GetString(bytes);
        return trimNulls ? value.TrimEnd('\0') : value;
    }

    private static void WriteFixedString(Stream stream, string value, int length)
    {
        byte[] destination = new byte[length];
        Encoding.ASCII.GetBytes(value.AsSpan(0, Math.Min(value.Length, length)), destination);
        stream.Write(destination);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        ReadExactly(stream, bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static float ReadSingle(Stream stream) => BitConverter.UInt32BitsToSingle(ReadUInt32(stream));

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteSingle(Stream stream, float value) => WriteUInt32(stream, BitConverter.SingleToUInt32Bits(value));

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int current = stream.Read(destination[read..]);
            if (current == 0)
                throw new EndOfStreamException();
            read += current;
        }
    }

    private static void SkipExactly(Stream stream, uint count)
    {
        if (count == 0)
            return;
        if (stream.CanSeek)
        {
            long target = checked(stream.Position + count);
            if (target > stream.Length)
                throw new EndOfStreamException();
            stream.Position = target;
            return;
        }

        byte[] buffer = new byte[Math.Min(count, 4096)];
        uint remaining = count;
        while (remaining > 0)
        {
            int toRead = checked((int)Math.Min((uint)buffer.Length, remaining));
            ReadExactly(stream, buffer.AsSpan(0, toRead));
            remaining -= checked((uint)toRead);
        }
    }

    [GeneratedRegex(@"^Moves(\d+)?(?:_|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovesPrefixRegex();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}(?:-\d{2})?)$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    private sealed record ParsedRec(
        string FormatName,
        uint Version,
        string MapName,
        Dictionary<uint, List<RecordedMotionSample>> SamplesByPad);

    private sealed record RecField(string Name, uint Size, bool IsList, RecFieldKind Kind);
    private readonly record struct InputDescription(uint PadId, uint FirstVectorIndex, uint VectorCount);

    private enum RecFieldKind
    {
        Unknown,
        Time,
        Float,
        Acceleration,
        Gyro,
        InputDescription
    }
}

internal enum RecMotionFormat
{
    Wii,
    Modern
}