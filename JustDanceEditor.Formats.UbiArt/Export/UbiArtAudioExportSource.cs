using KevInc.Audio.NAudio;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JustDanceEditor.Formats.UbiArt.Export;

public sealed record UbiArtAudioExportSource(
    string SourcePath,
    TimeSpan Start = default,
    TimeSpan? Duration = null,
    IReadOnlyList<int>? Markers = null)
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;

    public WaveStream OpenWaveStream()
    {
        using WaveStream source = OpenSourceWaveStream(SourcePath);

        ISampleProvider samples = source.ToSampleProvider();
        samples = EnsureStereo(samples);

        if (samples.WaveFormat.SampleRate != TargetSampleRate)
            samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);

        if (Start > TimeSpan.Zero || Duration.HasValue)
        {
            OffsetSampleProvider offset = new(samples);
            if (Start > TimeSpan.Zero)
                offset.SkipOver = Start;
            if (Duration.HasValue)
                offset.Take = Duration.Value;
            samples = offset;
        }

        return new Pcm16SampleProviderWaveStream(samples, TargetSampleRate, TargetChannels);
    }

    private static WaveStream OpenSourceWaveStream(string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".opus", StringComparison.OrdinalIgnoreCase))
            return new OpusWaveStream(sourcePath);

        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".wave", StringComparison.OrdinalIgnoreCase))
        {
            return new WaveFileReader(sourcePath);
        }

        throw new NotSupportedException($"Audio source '{sourcePath}' is not a supported JDI audio asset.");
    }

    private static ISampleProvider EnsureStereo(ISampleProvider source)
    {
        int channels = source.WaveFormat.Channels;
        return channels switch
        {
            1 => new MonoToStereoSampleProvider(source),
            2 => source,
            _ => new FirstTwoChannelsSampleProvider(source)
        };
    }

    private sealed class FirstTwoChannelsSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly ISampleProvider _source = source ?? throw new ArgumentNullException(nameof(source));
        private readonly float[] _sourceBuffer = new float[8192];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, TargetChannels);

        public int Read(float[] buffer, int offset, int count)
        {
            int sourceChannels = _source.WaveFormat.Channels;
            int requestedFrames = count / TargetChannels;
            int sourceSamplesNeeded = Math.Min(_sourceBuffer.Length, requestedFrames * sourceChannels);
            sourceSamplesNeeded -= sourceSamplesNeeded % sourceChannels;
            if (sourceSamplesNeeded <= 0)
                return 0;

            int sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            int framesRead = sourceSamplesRead / sourceChannels;
            for (int frame = 0; frame < framesRead; frame++)
            {
                int sourceIndex = frame * sourceChannels;
                int targetIndex = offset + frame * TargetChannels;
                buffer[targetIndex] = _sourceBuffer[sourceIndex];
                buffer[targetIndex + 1] = _sourceBuffer[sourceIndex + 1];
            }

            return framesRead * TargetChannels;
        }
    }
}