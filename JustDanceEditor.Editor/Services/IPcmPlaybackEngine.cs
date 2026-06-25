using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

internal interface IPcmPlaybackEngine : IDisposable
{
    event EventHandler? PlaybackCompleted;

    bool IsLoaded { get; }
    TimeSpan Duration { get; }
    bool IsMetronomeEnabled { get; set; }

    void Load(PcmWaveAudioData audio);
    void Play(TimeSpan startTime, bool completeAtAudioEnd);
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null);
}

internal interface IAudioClockPlaybackEngine
{
    bool TryGetPlaybackTime(out TimeSpan time);
}

internal sealed class AudioPlaybackUnavailableException : InvalidOperationException
{
    public AudioPlaybackUnavailableException(string message)
        : base(message)
    {
    }

    public AudioPlaybackUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}