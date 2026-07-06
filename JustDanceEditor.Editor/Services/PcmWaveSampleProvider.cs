using NAudio.Wave;

using System;

namespace JustDanceEditor.Editor.Services;

internal sealed class PcmWaveSampleProvider(PcmWaveAudioData audio) : ISampleProvider
{
    private long _positionFrames;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels);

    public TimeSpan TotalTime => audio.Duration;

    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds(_positionFrames / (double)audio.SampleRate);
        set => _positionFrames = SecondsToFrame(value.TotalSeconds);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int availableSamples = checked((int)Math.Min(
            count,
            Math.Max(0, audio.Samples.LongLength - (_positionFrames * audio.Channels))));

        int sourceOffset = checked((int)(_positionFrames * audio.Channels));
        for (int i = 0; i < availableSamples; i++)
            buffer[offset + i] = audio.Samples[sourceOffset + i] / 32768f;

        _positionFrames += availableSamples / audio.Channels;
        return availableSamples;
    }

    private long SecondsToFrame(double seconds)
    {
        double clampedSeconds = Math.Clamp(seconds, 0, audio.Duration.TotalSeconds);
        return (long)Math.Round(clampedSeconds * audio.SampleRate);
    }
}