using NAudio.Wave;

namespace JustDanceEditor.Audio.Tests;

/// <summary>
/// Helper class for generating test audio streams and patterns.
/// </summary>
public static class TestAudioHelper
{
    /// <summary>
    /// Test audio pattern types for synthetic audio generation.
    /// </summary>
    public enum TestPattern
    {
        /// <summary>Sine wave at 440Hz (A note)</summary>
        SineWave,
        /// <summary>Square wave at 440Hz</summary>
        SquareWave,
        /// <summary>Silence (all zeros)</summary>
        Silence,
        /// <summary>White noise</summary>
        WhiteNoise,
        /// <summary>Sweep from low to high frequency</summary>
        Sweep
    }

    /// <summary>
    /// Creates a test audio stream with the specified parameters and pattern.
    /// Default duration is 1 second at 48kHz stereo.
    /// </summary>
    public static WaveStream CreateTestAudio(int sampleRate = 48000, int channels = 2,
        float durationSeconds = 1f, TestPattern pattern = TestPattern.SineWave)
    {
        int samples = (int)(sampleRate * durationSeconds);
        float[] sampleData = GenerateAudioPattern(pattern, sampleRate, channels, samples);

        // Create a WaveFormat
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        // Create a MemoryStream containing WAV data
        MemoryStream stream = new();
        using (WaveFileWriter writer = new(stream, format))
        {
            writer.WriteSamples(sampleData, 0, sampleData.Length);
        }

        byte[] wavData = stream.ToArray();
        MemoryStream wavStream = new(wavData);

        // Return WaveFileReader wrapped in a stream that prevents closure
        return new SafeWaveFileReader(new WaveFileReader(wavStream));
    }

    /// <summary>
    /// Generates raw audio sample data in a specified pattern.
    /// </summary>
    private static float[] GenerateAudioPattern(TestPattern pattern, int sampleRate, int channels, int totalSamples)
    {
        float[] samples = new float[totalSamples * channels];
        Random random = new(42); // Fixed seed for reproducibility

        for (int i = 0; i < totalSamples; i++)
        {
            float sample = pattern switch
            {
                TestPattern.SineWave => (float)Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.5f,

                TestPattern.SquareWave =>
                    (Math.Sin(2 * Math.PI * 440 * i / sampleRate) >= 0 ? 0.5f : -0.5f),

                TestPattern.Silence => 0f,

                TestPattern.WhiteNoise => ((float)random.NextDouble() - 0.5f) * 0.1f,

                TestPattern.Sweep =>
                    (float)Math.Sin(2 * Math.PI * (440 + 440 * i / totalSamples) * i / sampleRate) * 0.5f,

                _ => 0f
            };

            // Write to all channels
            for (int c = 0; c < channels; c++)
            {
                samples[i * channels + c] = sample;
            }
        }

        return samples;
    }

    /// <summary>
    /// Extracts raw sample data from a WaveStream for comparison.
    /// </summary>
    public static float[] ExtractSamples(WaveStream source, int maxSamples = -1)
    {
        source.Position = 0;

        int sampleCount = maxSamples > 0
            ? maxSamples
            : (int)(source.Length / source.WaveFormat.BlockAlign);

        List<float> samples = [];
        byte[] buffer = new byte[65536];

        int bytesRead;
        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            // Convert bytes to float samples (assuming 16-bit PCM)
            int sampleCount16 = bytesRead / 2;
            for (int i = 0; i < sampleCount16; i++)
            {
                short sample = BitConverter.ToInt16(buffer, i * 2);
                samples.Add(sample / 32768f);
            }
        }

        return samples.ToArray();
    }

    /// <summary>
    /// Compares two sample arrays with tolerance for quantization errors.
    /// </summary>
    public static bool AssertSamplesClose(float[] expected, float[] actual,
        float tolerance = 0.01f, string message = "")
    {
        if (expected.Length != actual.Length)
            throw new AssertionException(
                $"Sample count mismatch: expected {expected.Length}, got {actual.Length}. {message}");

        int errorCount = 0;
        float maxError = 0f;

        for (int i = 0; i < expected.Length; i++)
        {
            float diff = Math.Abs(expected[i] - actual[i]);
            if (diff > tolerance)
            {
                errorCount++;
                maxError = Math.Max(maxError, diff);
            }
        }

        if (errorCount > 0)
        {
            double errorRate = (double)errorCount / expected.Length;
            if (errorRate > 0.05) // Allow up to 5% errors due to quantization
            {
                throw new AssertionException(
                    $"Sample comparison failed: {errorCount}/{expected.Length} samples exceeded tolerance. " +
                    $"Max error: {maxError}. {message}");
            }
        }

        return true;
    }

    /// <summary>
    /// Calculates RMS (Root Mean Square) error between two audio signals.
    /// </summary>
    public static float CalculateRmsError(float[] expected, float[] actual)
    {
        if (expected.Length != actual.Length)
            throw new ArgumentException("Sample counts must match");

        float sumSquares = 0f;
        for (int i = 0; i < expected.Length; i++)
        {
            float diff = expected[i] - actual[i];
            sumSquares += diff * diff;
        }

        return (float)Math.Sqrt(sumSquares / expected.Length);
    }

    /// <summary>
    /// Custom assertion exception for audio test failures.
    /// </summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }
}

/// <summary>
/// Wrapper around WaveFileReader that prevents disposal of the underlying reader,
/// allowing the test stream to be safely disposed without closing resources prematurely.
/// </summary>
internal sealed class SafeWaveFileReader : WaveStream
{
    private readonly WaveFileReader _reader;
    private bool _disposed;

    public SafeWaveFileReader(WaveFileReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public override WaveFormat WaveFormat => _reader.WaveFormat;
    public override long Length => _reader.Length;
    public override long Position
    {
        get => _reader.Position;
        set => _reader.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _reader.Read(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Don't dispose the underlying reader - let it stay open
            // This prevents "Cannot access closed stream" errors in tests
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}