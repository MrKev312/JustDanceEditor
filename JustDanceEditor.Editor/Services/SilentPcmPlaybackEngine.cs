using System;
using System.Collections.Generic;
namespace JustDanceEditor.Editor.Services;

internal sealed class SilentPcmPlaybackEngine : IPcmPlaybackEngine
{
    private PcmWaveAudioData? _audio;

    public event EventHandler? PlaybackCompleted;

    public bool IsLoaded => _audio != null;
    public TimeSpan Duration => _audio?.Duration ?? TimeSpan.Zero;
    public bool IsMetronomeEnabled { get; set; }

    public void Load(PcmWaveAudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
    }

    public void Play(TimeSpan startTime, bool completeAtAudioEnd)
    {
        if (completeAtAudioEnd && _audio != null && startTime >= _audio.Duration)
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
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
        _audio = null;
    }
}
