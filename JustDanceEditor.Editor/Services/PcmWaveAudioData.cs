using System;
using System.Buffers.Binary;

namespace JustDanceEditor.Editor.Services;

public sealed record PcmWaveAudioData(int SampleRate, int Channels, short[] Samples)
{
    public long FrameCount => Samples.Length / Math.Max(1, Channels);
    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)SampleRate);

    public static PcmWaveAudioData FromS16Le(int sampleRate, int channels, ReadOnlySpan<byte> bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        int sampleCount = bytes.Length / sizeof(short);
        sampleCount -= sampleCount % channels;

        short[] samples = new short[sampleCount];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(i * sizeof(short), sizeof(short)));

        return new PcmWaveAudioData(sampleRate, channels, samples);
    }
}