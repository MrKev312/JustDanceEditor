using NAudio.Wave;

namespace JustDanceEditor.Audio.Providers;

/// <summary>
/// Wraps an <see cref="ISampleProvider"/> so it never signals end-of-stream.
/// Once the known audio duration (in sample frames) is exceeded, all subsequent
/// reads return silence. Always returns the full requested <c>count</c>.
/// Call <see cref="Reset"/> after seeking back into the audio range.
/// </summary>
/// <param name="source">The audio sample source.</param>
/// <param name="sourceDuration">Exact duration of the audio file.</param>
public sealed class EndlessSampleProvider(ISampleProvider source, TimeSpan sourceDuration) : ISampleProvider
{
    private readonly int _channels = source.WaveFormat.Channels;
    private readonly long _totalFrames = (long)(sourceDuration.TotalSeconds * source.WaveFormat.SampleRate);
    private long _framesRead;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        // Always clear the full buffer first — WaveOutEvent reuses the same buffer and may play
        // beyond 'count' samples, causing stale audio data to loop if we don't clear it.
        Array.Clear(buffer);

        long framesRemaining = _totalFrames - _framesRead;
        int maxSamples = framesRemaining > 0
            ? (int)Math.Min(count, framesRemaining * _channels)
            : 0;

        int read = 0;
        if (maxSamples > 0)
            read = source.Read(buffer, offset, maxSamples);

        int framesConsumed = (read > 0 ? read : count) / _channels;
        _framesRead += framesConsumed;

        return count;
    }

    /// <summary>
    /// Resets the frame counter to match a seek position.
    /// Call this after seeking the underlying audio stream.
    /// </summary>
    /// <param name="timeSeconds">The time position being seeked to.</param>
    public void Reset(double timeSeconds)
    {
        _framesRead = (long)(timeSeconds * source.WaveFormat.SampleRate);
    }
}
