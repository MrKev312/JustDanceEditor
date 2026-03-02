using NAudio.Wave;

namespace JustDanceEditor.Audio.Providers;

/// <summary>
/// An <see cref="ISampleProvider"/> wrapper that stops returning samples after a fixed duration.
/// Returns 0 once the duration is exhausted, signaling end-of-stream to NAudio.
/// </summary>
public sealed class TruncatingSampleProvider(ISampleProvider source, double maxDurationSeconds) : ISampleProvider
{
    private readonly long _maxSamples = (long)(maxDurationSeconds * source.WaveFormat.SampleRate * source.WaveFormat.Channels);
    private long _samplesRead;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        long remaining = _maxSamples - _samplesRead;
        if (remaining <= 0)
            return 0;

        int toRead = (int)Math.Min(count, remaining);
        int read = source.Read(buffer, offset, toRead);
        _samplesRead += read;
        return read;
    }
}