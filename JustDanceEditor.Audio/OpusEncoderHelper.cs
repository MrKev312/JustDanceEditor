using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JustDanceEditor.Audio;

/// <summary>
/// Helper class to encode audio to Opus format using Concentus.
/// </summary>
public static class OpusEncoderHelper
{
    /// <summary>
    /// Encodes a sample provider to Opus format and writes to the output stream.
    /// </summary>
    /// <param name="sampleProvider">The audio source to encode.</param>
    /// <param name="outputStream">The stream to write the Opus data to.</param>
    /// <param name="bitrate">The bitrate in bits per second (default 128000).</param>
    public static void EncodeToOpus(ISampleProvider sampleProvider, Stream outputStream, int bitrate = 128000)
    {
        ArgumentNullException.ThrowIfNull(sampleProvider);
        ArgumentNullException.ThrowIfNull(outputStream);

        // Opus requires 48kHz. Resample if necessary.
        ISampleProvider resampled = sampleProvider;
        if (sampleProvider.WaveFormat.SampleRate != 48000)
        {
            resampled = new WdlResamplingSampleProvider(sampleProvider, 48000);
        }

        // Ensure we have stereo (Opus works best with 1 or 2 channels)
        int channels = resampled.WaveFormat.Channels;
        if (channels > 2)
        {
            // Take only first 2 channels if more than 2
            resampled = resampled.ToMono().ToStereo();
            channels = 2;
        }

        // Use the factory so native Concentus can be selected when available.
        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, channels, OpusApplication.OPUS_APPLICATION_AUDIO, TextWriter.Null);
        encoder.Bitrate = bitrate;

        // Wrap the stream to prevent OpusOggWriteStream from closing it
        NonClosingStreamWrapper wrappedStream = new(outputStream);
        OpusOggWriteStream oggWriter = new(encoder, wrappedStream, null, 48000);

        // Read samples and encode
        const int samplesPerFrame = 960; // 20ms at 48kHz
        float[] buffer = new float[samplesPerFrame * channels];

        int samplesRead;
        while ((samplesRead = resampled.Read(buffer, 0, buffer.Length)) > 0)
        {
            // Pad with zeros if we got fewer samples
            if (samplesRead < buffer.Length)
            {
                Array.Clear(buffer, samplesRead, buffer.Length - samplesRead);
            }

            oggWriter.WriteSamples(buffer, 0, buffer.Length);
        }

        oggWriter.Finish();
    }

    /// <summary>
    /// Encodes a WaveStream to Opus format and returns the result as a byte array.
    /// </summary>
    /// <param name="waveStream">The audio source to encode.</param>
    /// <param name="bitrate">The bitrate in bits per second (default 128000).</param>
    /// <returns>The encoded Opus data as a byte array.</returns>
    public static byte[] EncodeToOpusBytes(WaveStream waveStream, int bitrate = 128000)
    {
        using MemoryStream ms = new();
        EncodeToOpus(waveStream.ToSampleProvider(), ms, bitrate);
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes a sample provider to Opus format and returns the result as a MemoryStream.
    /// </summary>
    /// <param name="sampleProvider">The audio source to encode.</param>
    /// <param name="bitrate">The bitrate in bits per second (default 128000).</param>
    /// <returns>A MemoryStream containing the encoded Opus data.</returns>
    public static MemoryStream EncodeToOpusStream(ISampleProvider sampleProvider, int bitrate = 128000)
    {
        MemoryStream ms = new();
        EncodeToOpus(sampleProvider, ms, bitrate);
        ms.Position = 0;
        return ms;
    }
}

/// <summary>
/// Wrapper that prevents the underlying stream from being closed.
/// Used to wrap streams that will be disposed by third-party code.
/// </summary>
internal sealed class NonClosingStreamWrapper : Stream
{
    private readonly Stream _underlyingStream;
    private bool _disposed;

    public NonClosingStreamWrapper(Stream underlyingStream)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);
        _underlyingStream = underlyingStream;
    }

    public override bool CanRead => _underlyingStream.CanRead;
    public override bool CanSeek => _underlyingStream.CanSeek;
    public override bool CanWrite => _underlyingStream.CanWrite;
    public override long Length => _underlyingStream.Length;

    public override long Position
    {
        get => _underlyingStream.Position;
        set => _underlyingStream.Position = value;
    }

    public override void Flush() => _underlyingStream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _underlyingStream.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _underlyingStream.Seek(offset, origin);
    public override void SetLength(long value) => _underlyingStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _underlyingStream.Write(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _underlyingStream.ReadAsync(buffer, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _underlyingStream.WriteAsync(buffer, cancellationToken);

    // Override Close and Dispose to prevent closing the underlying stream
    public override void Close()
    {
        // Don't close the underlying stream
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Don't dispose the underlying stream - let the caller manage it
                Flush();
            }

            _disposed = true;
        }
        // Don't call base.Dispose() to avoid closing the stream
    }
}