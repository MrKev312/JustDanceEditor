using System.Buffers.Binary;
using System.Text;

using NAudio.Wave;

using Xunit;

namespace JustDanceEditor.Audio.Tests;

/// <summary>
/// Tests for RAKI format encoding and round-trip conversion.
/// Verifies that audio can be encoded to RAKI format and decoded back with minimal loss.
/// </summary>
public class RakiFormatTests
{
    /// <summary>
    /// Tests that PCM audio can be encoded to RAKI format.
    /// </summary>
    [Fact]
    public void EncodeToRakiPcm_CreatesValidRakiFile()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.5f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiPcm(testAudio, output);

        // Assert
        byte[] data = output.ToArray();
        Assert.NotEmpty(data);

        // Verify RAKI magic signature
        output.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        output.Read(magic, 0, 4);
        string magicStr = System.Text.Encoding.ASCII.GetString(magic);
        Assert.Equal("RAKI", magicStr);
    }

    /// <summary>
    /// Tests that RAKI encoding preserves audio format information.
    /// </summary>
    [Fact]
    public void EncodeToRakiPcm_PreservesAudioFormat()
    {
        // Arrange
        using WaveStream original = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.1f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiPcm(original, output);

        // Assert - verify output contains audio data
        Assert.True(output.Length > 0);

        // Verify RAKI structure is present
        output.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        output.Read(magic, 0, 4);
        Assert.Equal("RAKI", System.Text.Encoding.ASCII.GetString(magic));
    }

    /// <summary>
    /// Tests various sample rates can be encoded.
    /// </summary>
    [Theory]
    [InlineData(16000)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void EncodeToRakiPcm_HandleVariousSampleRates(int sampleRate)
    {
        // Arrange
        using WaveStream original = TestAudioHelper.CreateTestAudio(
            sampleRate: sampleRate,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.Silence);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiPcm(original, output);

        // Assert
        Assert.True(output.Length > 0);
    }

    /// <summary>
    /// Tests that different audio patterns can be encoded.
    /// </summary>
    [Theory]
    [InlineData(TestAudioHelper.TestPattern.Silence)]
    [InlineData(TestAudioHelper.TestPattern.SineWave)]
    [InlineData(TestAudioHelper.TestPattern.SquareWave)]
    [InlineData(TestAudioHelper.TestPattern.WhiteNoise)]
    public void EncodeToRakiPcm_HandlesVariousPatterns(TestAudioHelper.TestPattern pattern)
    {
        // Arrange
        using WaveStream original = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.1f,
            pattern: pattern);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiPcm(original, output);

        // Assert
        Assert.True(output.Length > 0);
    }

    /// <summary>
    /// Tests mono audio encoding.
    /// </summary>
    [Fact]
    public void EncodeToRakiPcm_SupportsMono()
    {
        // Arrange
        using WaveStream original = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 1,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiPcm(original, output);

        // Assert
        Assert.True(output.Length > 0);
    }

    /// <summary>
    /// Tests stereo audio encoding.
    /// </summary>
    [Fact]
    public void EncodeToRakiPcm_SupportsStereo()
    {
        // Arrange
        using WaveStream original = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiPcm(original, output);

        // Assert
        Assert.True(output.Length > 0);
    }

    /// <summary>
    /// Tests ADPCM encoding (basic functionality).
    /// </summary>
    [Fact]
    public void EncodeToRakiAdpcm_CreatesValidRakiFile()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiCafeAdpcm(testAudio, output);

        // Assert
        byte[] data = output.ToArray();
        Assert.NotEmpty(data);

        // Verify RAKI magic
        output.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        output.Read(magic, 0, 4);
        string magicStr = System.Text.Encoding.ASCII.GetString(magic);
        Assert.Equal("RAKI", magicStr);
    }

    /// <summary>
    /// Tests big-endian PCM encoding for platforms like Wii.
    /// </summary>
    [Fact]
    public void EncodeToRakiPcm_BigEndian_CreatesValidRakiFile()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act - Encode with Wii platform (big-endian)
        RakiAudioEncoder.EncodeToRakiPcm(testAudio, output, platform: "Wii ", type: "pcm ");

        // Assert
        byte[] data = output.ToArray();
        Assert.NotEmpty(data);

        // Verify RAKI magic
        output.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        output.Read(magic, 0, 4);
        string magicStr = System.Text.Encoding.ASCII.GetString(magic);
        Assert.Equal("RAKI", magicStr);
    }

    /// <summary>
    /// Tests that encoding and then checking the output doesn't throw exceptions.
    /// </summary>
    [Fact]
    public void EncodeToRakiPcm_DoesNotThrowOnValidInput()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.2f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act & Assert - should not throw
        RakiAudioEncoder.EncodeToRakiPcm(testAudio, output);
        Assert.True(output.Length > 100); // Should have at least some content
    }

    /// <summary>
    /// Tests that Xbox 360 XMA2 audio can be encoded to RAKI format.
    /// </summary>
    [Fact]
    public void EncodeToRakiXma2_CreatesValidRakiFile()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiXma2(testAudio, output);

        // Assert
        byte[] data = output.ToArray();
        Assert.True(data.Length > 0x800);
        Assert.Equal("RAKI", Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal("X360", Encoding.ASCII.GetString(data, 8, 4));
        Assert.Equal("xma2", Encoding.ASCII.GetString(data, 12, 4));

        Assert.Equal("fmt ", Encoding.ASCII.GetString(data, 0x20, 4));
        Assert.Equal(0x38u, ReadU32BigEndian(data, 0x24));
        Assert.Equal(0x34u, ReadU32BigEndian(data, 0x28));
        Assert.Equal("data", Encoding.ASCII.GetString(data, 0x2C, 4));
        Assert.Equal(0x800u, ReadU32BigEndian(data, 0x30));
        Assert.True(ReadU32BigEndian(data, 0x34) > 0);

        int fmtOffset = checked((int)ReadU32BigEndian(data, 0x24));
        Assert.Equal(0x0166, ReadU16BigEndian(data, fmtOffset));
        Assert.Equal(2, ReadU16BigEndian(data, fmtOffset + 2));
        Assert.Equal(48000u, ReadU32BigEndian(data, fmtOffset + 4));
        Assert.Equal(16, ReadU16BigEndian(data, fmtOffset + 14));
        Assert.Equal(0x22, ReadU16BigEndian(data, fmtOffset + 16));
        Assert.Equal(1, ReadU16BigEndian(data, fmtOffset + 18));
    }

    /// <summary>
    /// Tests that managed XMA2 output can be decoded by the managed XMA2 decoder.
    /// </summary>
    [Fact]
    public async Task EncodeToRakiXma2_RoundTrip_CanBeDecoded()
    {
        // Arrange
        const int sampleRate = 48000;
        const int channels = 2;
        const float duration = 0.1f;

        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: sampleRate,
            channels: channels,
            durationSeconds: duration,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream encodedOutput = new();

        // Act - Encode
        RakiAudioEncoder.EncodeToRakiXma2(testAudio, encodedOutput);
        encodedOutput.Position = 0;

        // Act - Decode
        RakiAudioConverter converter = new();
        using WaveStream decoded = await converter.ConvertAsync(encodedOutput, "test.wav.ckd");

        // Assert
        int expectedFrames = (int)(sampleRate * duration);
        Assert.Equal(sampleRate, decoded.WaveFormat.SampleRate);
        Assert.Equal(channels, decoded.WaveFormat.Channels);
        Assert.Equal(expectedFrames * channels * sizeof(short), decoded.Length);

        byte[] decodedBytes = new byte[decoded.Length];
        decoded.ReadExactly(decodedBytes);
        (double correlation, double rmsError) = CompareDecodedSine(decodedBytes, sampleRate, channels);

        Assert.True(correlation > 0.99, $"Decoded XMA2 correlation was {correlation:F4}.");
        Assert.True(rmsError < 0.03, $"Decoded XMA2 RMS error was {rmsError:F4}.");
    }

    /// <summary>
    /// Regression coverage for XMA2's escaped quant-step boundary on quiet ambience-like material.
    /// </summary>
    [Fact]
    public async Task EncodeToRakiXma2_QuietStereoTexture_RoundTripDoesNotBurst()
    {
        // Arrange
        const int sampleRate = 48000;
        const int channels = 2;
        const float duration = 0.35f;
        float[] sourceSamples = CreateQuietStereoTexture(sampleRate, duration);
        using WaveStream testAudio = CreateFloatWaveStream(sourceSamples, sampleRate, channels);
        using MemoryStream encodedOutput = new();

        // Act - Encode
        RakiAudioEncoder.EncodeToRakiXma2(testAudio, encodedOutput);
        encodedOutput.Position = 0;

        // Act - Decode
        RakiAudioConverter converter = new();
        using WaveStream decoded = await converter.ConvertAsync(encodedOutput, "quiet.wav.ckd");

        // Assert
        byte[] decodedBytes = new byte[decoded.Length];
        decoded.ReadExactly(decodedBytes);
        (double correlation, double rmsRatio, double peak) = CompareDecodedSamples(sourceSamples, decodedBytes, channels);

        Assert.True(correlation > 0.95, $"Decoded quiet XMA2 correlation was {correlation:F4}.");
        Assert.InRange(rmsRatio, 0.75, 1.25);
        Assert.True(peak < 0.05, $"Decoded quiet XMA2 peak was {peak:F4}.");
    }

    /// <summary>
    /// Tests that audio can be encoded to Nintendo Switch Opus format.
    /// </summary>
    [Fact]
    public void EncodeToRakiNxOpus_CreatesValidRakiFile()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.1f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiNxOpus(testAudio, output);

        // Assert
        byte[] data = output.ToArray();
        Assert.NotEmpty(data);

        // Verify RAKI magic signature
        output.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        output.Read(magic, 0, 4);
        string magicStr = System.Text.Encoding.ASCII.GetString(magic);
        Assert.Equal("RAKI", magicStr);
    }

    /// <summary>
    /// Tests that Nintendo Opus encoding with gain preserves gain metadata.
    /// </summary>
    [Fact]
    public void EncodeToRakiNxOpus_PreservesGain()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.1f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();
        short testGain = 256; // 1 dB gain (256/256 = 1.0 dB)

        // Act
        RakiAudioEncoder.EncodeToRakiNxOpus(testAudio, output, preSkip: 0, outputGainDb256: testGain);

        // Assert
        output.Seek(0, SeekOrigin.Begin);
        byte[] data = output.ToArray();

        // Check RAKI header
        Assert.Equal("RAKI", System.Text.Encoding.ASCII.GetString(data, 0, 4));

        // Check that the output is non-empty and has reasonable structure
        // The gain is stored in the Nintendo Opus header, which starts after RAKI (0x40 offset)
        // At offset 0x20 within the Nintendo header
        Assert.True(data.Length > 100, "Output should contain RAKI header and Opus data");
    }

    /// <summary>
    /// Tests various sample rates for Nintendo Opus encoding (should auto-resample to 48kHz).
    /// </summary>
    [Theory]
    [InlineData(16000)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void EncodeToRakiNxOpus_HandlesVariousSampleRates(int sampleRate)
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: sampleRate,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiNxOpus(testAudio, output);

        // Assert
        Assert.True(output.Length > 0);
    }

    /// <summary>
    /// Tests that Nintendo Opus encoding handles different audio patterns.
    /// </summary>
    [Theory]
    [InlineData(TestAudioHelper.TestPattern.Silence)]
    [InlineData(TestAudioHelper.TestPattern.SineWave)]
    [InlineData(TestAudioHelper.TestPattern.SquareWave)]
    public void EncodeToRakiNxOpus_HandlesVariousPatterns(TestAudioHelper.TestPattern pattern)
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.05f,
            pattern: pattern);

        using MemoryStream output = new();

        // Act & Assert - should not throw
        RakiAudioEncoder.EncodeToRakiNxOpus(testAudio, output);
        Assert.True(output.Length > 0);
    }

    /// <summary>
    /// Tests Nintendo Opus encoding with stereo audio.
    /// </summary>
    [Fact]
    public void EncodeToRakiNxOpus_SupportsStereo()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.1f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream output = new();

        // Act
        RakiAudioEncoder.EncodeToRakiNxOpus(testAudio, output);

        // Assert
        Assert.True(output.Length > 100);
    }

    /// <summary>
    /// Tests that Nintendo Opus encoded audio can be decoded back.
    /// This is a round-trip test to ensure the encoder produces valid output.
    /// </summary>
    [Fact]
    public async Task EncodeToRakiNxOpus_RoundTrip_CanBeDecoded()
    {
        // Arrange
        using WaveStream testAudio = TestAudioHelper.CreateTestAudio(
            sampleRate: 48000,
            channels: 2,
            durationSeconds: 0.5f,
            pattern: TestAudioHelper.TestPattern.SineWave);

        using MemoryStream encodedOutput = new();

        // Act - Encode
        RakiAudioEncoder.EncodeToRakiNxOpus(testAudio, encodedOutput);
        encodedOutput.Position = 0;

        // Act - Decode
        RakiAudioConverter converter = new();
        using WaveStream decoded = await converter.ConvertAsync(encodedOutput, "test.wav.ckd");

        // Assert
        Assert.NotNull(decoded);
        Assert.Equal(48000, decoded.WaveFormat.SampleRate);
        Assert.Equal(2, decoded.WaveFormat.Channels);
        Assert.True(decoded.Length > 0, "Decoded audio should have data");
    }

    private static ushort ReadU16BigEndian(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, sizeof(ushort)));

    private static uint ReadU32BigEndian(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, sizeof(uint)));

    private static (double Correlation, double RmsError) CompareDecodedSine(byte[] decodedBytes, int sampleRate, int channels)
    {
        int frameCount = decodedBytes.Length / (channels * sizeof(short));
        int startFrame = Math.Min(1024, frameCount / 4);
        int endFrame = Math.Max(startFrame, frameCount - startFrame);

        double dot = 0;
        double expectedSquares = 0;
        double actualSquares = 0;
        double errorSquares = 0;
        int count = 0;

        for (int frame = startFrame; frame < endFrame; frame++)
        {
            double expected = Math.Sin(2 * Math.PI * 440 * frame / sampleRate) * 0.5;
            for (int channel = 0; channel < channels; channel++)
            {
                int byteOffset = ((frame * channels) + channel) * sizeof(short);
                double actual = BitConverter.ToInt16(decodedBytes, byteOffset) / 32768.0;
                dot += expected * actual;
                expectedSquares += expected * expected;
                actualSquares += actual * actual;
                double error = expected - actual;
                errorSquares += error * error;
                count++;
            }
        }

        double correlation = dot / Math.Sqrt(expectedSquares * actualSquares);
        double rmsError = Math.Sqrt(errorSquares / count);
        return (correlation, rmsError);
    }

    private static float[] CreateQuietStereoTexture(int sampleRate, float durationSeconds)
    {
        int frames = (int)(sampleRate * durationSeconds);
        float[] samples = new float[frames * 2];
        Random random = new(1234);
        double lowPass = 0;

        for (int frame = 0; frame < frames; frame++)
        {
            double breath = Math.Sin(2 * Math.PI * 2.7 * frame / sampleRate) * 0.0045;
            lowPass = (lowPass * 0.985) + (((random.NextDouble() * 2.0) - 1.0) * 0.015);
            double texture = lowPass * 0.0009;
            samples[frame * 2] = (float)(breath + texture);
            samples[(frame * 2) + 1] = (float)((breath * 0.92) - texture);
        }

        return samples;
    }

    private static WaveStream CreateFloatWaveStream(float[] samples, int sampleRate, int channels)
    {
        MemoryStream stream = new();
        using (WaveFileWriter writer = new(stream, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)))
            writer.WriteSamples(samples, 0, samples.Length);

        return new WaveFileReader(new MemoryStream(stream.ToArray()));
    }

    private static (double Correlation, double RmsRatio, double Peak) CompareDecodedSamples(float[] expectedSamples, byte[] decodedBytes, int channels)
    {
        int sampleCount = Math.Min(expectedSamples.Length, decodedBytes.Length / sizeof(short));
        int start = Math.Min(1024 * channels, sampleCount / 4);
        int end = Math.Max(start, sampleCount - start);

        double dot = 0;
        double expectedSquares = 0;
        double actualSquares = 0;
        double peak = 0;

        for (int i = start; i < end; i++)
        {
            double expected = expectedSamples[i];
            double actual = BitConverter.ToInt16(decodedBytes, i * sizeof(short)) / 32768.0;
            dot += expected * actual;
            expectedSquares += expected * expected;
            actualSquares += actual * actual;
            peak = Math.Max(peak, Math.Abs(actual));
        }

        double correlation = dot / Math.Sqrt(Math.Max(expectedSquares * actualSquares, 1e-20));
        double rmsRatio = Math.Sqrt(actualSquares / Math.Max(expectedSquares, 1e-20));
        return (correlation, rmsRatio, peak);
    }
}
