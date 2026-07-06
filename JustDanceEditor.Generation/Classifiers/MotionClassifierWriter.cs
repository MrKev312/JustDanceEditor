using System.Text;

namespace JustDanceEditor.Generation;

internal static class MotionClassifierWriter
{
    public static byte[] Write(MotionClassifierData classifier, ulong measureSetBitfield, MotionClassifierGenerationOptions options)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(1U);
        writer.Write(options.ClassifierFormatVersion);
        WriteFixedString(writer, classifier.ModelName, 64);
        WriteFixedString(writer, classifier.SongName, 64);
        WriteFixedString(writer, classifier.MeasureSetName, 64);
        writer.Write(classifier.Duration);
        writer.Write(options.LowThreshold);
        writer.Write(options.HighThreshold);

        if (options.ClassifierFormatVersion >= 7)
        {
            writer.Write(options.AutoCorrelationThreshold);
            writer.Write(options.DirectionImpactFactor);
        }

        writer.Write(measureSetBitfield);
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

    private static void WriteFixedString(BinaryWriter writer, string value, int length)
    {
        byte[] result = new byte[length];
        byte[] source = Encoding.UTF8.GetBytes(value);
        Buffer.BlockCopy(source, 0, result, 0, Math.Min(source.Length, result.Length));
        writer.Write(result);
    }
}