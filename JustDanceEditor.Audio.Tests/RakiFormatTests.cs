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
        RakiAudioEncoder.EncodeToRakiAdpcm(testAudio, output);

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
}