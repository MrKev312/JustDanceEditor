using System;
using System.Collections.Generic;
using System.IO;

namespace JustDanceEditor.Editor.Services;

internal sealed class SilentPcmPlaybackEngine : IPcmPlaybackEngine
{
    private PcmWaveAudioData? _audio;

    public event EventHandler? PlaybackCompleted;

    public bool IsLoaded => _audio != null;
    public TimeSpan Duration => _audio?.Duration ?? TimeSpan.Zero;
    public bool IsMetronomeEnabled { get; set; }

    public void Load(string wavPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("The audio preview file does not exist.", wavPath);

        _audio = PcmWaveAudioData.Read(wavPath);
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

    public void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null)
    {
    }

    public void Dispose()
    {
        _audio = null;
    }
}
