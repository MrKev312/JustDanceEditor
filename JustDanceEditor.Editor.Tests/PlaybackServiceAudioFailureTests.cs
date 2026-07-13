using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.Tests;

public sealed class PlaybackServiceAudioFailureTests
{
    [Fact]
    public async Task Play_WhenPcmBackendFails_DoesNotThrow()
    {
        PcmWaveAudioData audio = new(48000, 2, new short[48000 * 2]);
        using PlaybackService playback = new(() => new ThrowingPcmPlaybackEngine(), useWindowsAudio: false);
        await playback.LoadMediaAsync(audio, beat => beat * 0.5, seconds => seconds / 0.5);

        Exception? exception = Record.Exception(playback.Play);

        Assert.Null(exception);
        Assert.True(playback.IsPlaying);
    }

    private sealed class ThrowingPcmPlaybackEngine : IPcmPlaybackEngine
    {
        public event EventHandler? PlaybackCompleted
        {
            add { }
            remove { }
        }

        public bool IsLoaded => true;
        public TimeSpan Duration => TimeSpan.FromSeconds(10);
        public bool IsMetronomeEnabled { get; set; }

        public void Load(PcmWaveAudioData audio)
        {
        }

        public void Play(TimeSpan startTime, bool completeAtAudioEnd)
        {
            throw new AudioPlaybackUnavailableException("No test device.");
        }

        public void Pause()
        {
        }

        public void Stop()
        {
        }

        public void Seek(TimeSpan position)
        {
        }

        public void UpdateMetronome(
            double zeroBeatTimeSeconds,
            double bpm,
            int beatsPerMeasure,
            IEnumerable<double>? sectionStarts = null,
            Func<double, double>? beatToSeconds = null,
            Func<double, double>? secondsToBeat = null)
        {
        }

        public void Dispose()
        {
        }
    }
}