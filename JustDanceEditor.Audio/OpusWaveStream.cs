using NAudio.Wave;

namespace JustDanceEditor.Audio;

/// <summary>
/// A bridge class to make Concentus Opus data look like a standard WaveStream to NAudio.
/// This allows Opus audio to be used with NAudio's mixing and processing capabilities.
/// 
/// Gain Handling: The OpusWaveStream reads the output gain field from the Opus header
/// and Concentus (OpusOggReadStream) applies it automatically during decoding, so no
/// additional gain adjustment is needed here. The gain is preserved in dB units as per
/// the Opus specification (1/256 dB per unit).
/// </summary>
public sealed class OpusWaveStream : WaveStream
{
    private readonly Stream _sourceStream;
    private readonly bool _ownsStream;
    private readonly Concentus.Oggfile.OpusOggReadStream _oggReader;
    private readonly Concentus.Structs.OpusDecoder _decoder;
    private readonly WaveFormat _waveFormat;
    private readonly MemoryStream _buffer;
    private long _position;
    private bool _disposed;

    /// <summary>
    /// Creates an OpusWaveStream from a file path.
    /// </summary>
    /// <param name="fileName">Path to the Opus/Ogg file.</param>
    public OpusWaveStream(string fileName)
        : this(System.IO.File.OpenRead(fileName), ownsStream: true)
    {
    }

    /// <summary>
    /// Creates an OpusWaveStream from an existing stream.
    /// </summary>
    /// <param name="stream">The source stream containing Opus audio data.</param>
    /// <param name="ownsStream">If true, the stream will be disposed when this object is disposed.</param>
    public OpusWaveStream(Stream stream, bool ownsStream = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _sourceStream = stream;
        _ownsStream = ownsStream;

        // Opus standard: 48kHz, stereo
#pragma warning disable CS0618 // Type or member is obsolete
        _decoder = new Concentus.Structs.OpusDecoder(48000, 2);
#pragma warning restore CS0618
        _oggReader = new Concentus.Oggfile.OpusOggReadStream(_decoder, _sourceStream);

        // NAudio format: 48kHz, 16-bit, stereo
        _waveFormat = new WaveFormat(48000, 16, 2);
        _buffer = new MemoryStream();
    }

    public override WaveFormat WaveFormat => _waveFormat;

    // Length is approximate since we can't easily know the uncompressed size without decoding
    public override long Length => _sourceStream.Length * 20; // Rough estimate: Opus ~20:1 compression

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("Seeking is not supported in OpusWaveStream");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Decode more packets if we don't have enough data in our buffer
        while (_buffer.Length - _buffer.Position < count)
        {
            if (!_oggReader.HasNextPacket)
                break;

            short[]? decodedSamples = _oggReader.DecodeNextPacket();
            if (decodedSamples != null && decodedSamples.Length > 0)
            {
                byte[] byteBuffer = new byte[decodedSamples.Length * 2];
                System.Buffer.BlockCopy(decodedSamples, 0, byteBuffer, 0, byteBuffer.Length);

                long oldPos = _buffer.Position;
                _buffer.Seek(0, SeekOrigin.End);
                _buffer.Write(byteBuffer, 0, byteBuffer.Length);
                _buffer.Position = oldPos;
            }
        }

        int bytesRead = _buffer.Read(buffer, offset, count);
        _position += bytesRead;
        return bytesRead;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _buffer.Dispose();
                if (_ownsStream)
                {
                    _sourceStream.Dispose();
                }
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}