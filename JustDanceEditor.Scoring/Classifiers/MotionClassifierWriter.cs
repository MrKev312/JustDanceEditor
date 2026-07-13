using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Scoring;

internal static class MotionClassifierWriter
{
    public static byte[] Write(MotionClassifierData classifier, ulong measureSetBitfield, MotionClassifierGenerationOptions options)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(1U);
        writer.Write((uint)options.ClassifierFormatVersion);
        WriteFixedString(writer, classifier.ModelName, 64);
        WriteFixedString(writer, classifier.SongName, 64);
        WriteFixedString(writer, classifier.MeasureSetName, 64);
        writer.Write(classifier.Duration);
        writer.Write(options.LowThreshold);
        writer.Write(options.HighThreshold);

        if (MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(options.ClassifierFormatVersion))
        {
            writer.Write(options.AutoCorrelationThreshold);
            writer.Write(options.DirectionImpactFactor);
        }

        writer.Write(measureSetBitfield);
        if (MotionClassifierFormatRules.HasCustomizationBitField(options.ClassifierFormatVersion))
            writer.Write(options.CustomizationBitField);
        writer.Write((uint)classifier.Means.Length);
        writer.Write((uint)classifier.EnergyMeans.Length);
        writer.Write(0U);

        foreach (double mean in classifier.Means)
            writer.Write((float)mean);

        foreach (double variance in classifier.Variances)
            writer.Write((float)(1.0 / variance));

        foreach (double energyMean in classifier.EnergyMeans)
            writer.Write((float)energyMean);

        return stream.ToArray();
    }

    public static byte[] Write(MotionClassifier classifier)
    {
        using MemoryStream stream = new();
        EndianWriter writer = new(stream, classifier.IsBigEndian);

        writer.Write(1U);
        writer.Write((uint)classifier.FormatVersion);
        writer.WriteFixedString(classifier.MoveName, 64);
        writer.WriteFixedString(classifier.SongName, 64);
        writer.WriteFixedString(classifier.MeasureSetName, 64);
        writer.Write(classifier.Duration);
        writer.Write(classifier.LowThreshold);
        writer.Write(classifier.HighThreshold);

        if (MotionClassifierFormatRules.HasAutoCorrelationAndDirectionFields(classifier.FormatVersion))
        {
            writer.Write(classifier.AutoCorrelationThreshold);
            writer.Write(classifier.DirectionImpactFactor);
        }

        writer.Write(classifier.MeasureSetBitfield);
        if (MotionClassifierFormatRules.HasCustomizationBitField(classifier.FormatVersion))
            writer.Write(classifier.CustomizationBitField);
        writer.Write(classifier.ScoringAlgorithmType);
        writer.Write(checked((uint)classifier.EnergyMeans.Length));
        writer.Write(classifier.SubClassifiersCount);

        foreach (float mean in classifier.Means)
            writer.Write(mean);

        foreach (float invertedCovariance in classifier.InvertedCovariances)
            writer.Write(invertedCovariance);

        foreach (float energyMean in classifier.EnergyMeans)
            writer.Write(energyMean);

        return stream.ToArray();
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int length)
    {
        byte[] result = new byte[length];
        byte[] source = Encoding.UTF8.GetBytes(value);
        Buffer.BlockCopy(source, 0, result, 0, Math.Min(source.Length, result.Length));
        writer.Write(result);
    }

    private sealed class EndianWriter(Stream stream, bool isBigEndian)
    {
        private readonly byte[] _buffer = new byte[sizeof(ulong)];

        public void Write(uint value)
        {
            Span<byte> destination = _buffer.AsSpan(0, sizeof(uint));
            if (isBigEndian)
                BinaryPrimitives.WriteUInt32BigEndian(destination, value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
            stream.Write(destination);
        }

        public void Write(int value)
        {
            Span<byte> destination = _buffer.AsSpan(0, sizeof(int));
            if (isBigEndian)
                BinaryPrimitives.WriteInt32BigEndian(destination, value);
            else
                BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            stream.Write(destination);
        }

        public void Write(ulong value)
        {
            Span<byte> destination = _buffer;
            if (isBigEndian)
                BinaryPrimitives.WriteUInt64BigEndian(destination, value);
            else
                BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
            stream.Write(destination);
        }

        public void Write(float value) => Write(BitConverter.SingleToInt32Bits(value));

        public void WriteFixedString(string value, int length)
        {
            byte[] result = new byte[length];
            byte[] source = Encoding.UTF8.GetBytes(value);
            Buffer.BlockCopy(source, 0, result, 0, Math.Min(source.Length, result.Length));
            stream.Write(result);
        }
    }
}