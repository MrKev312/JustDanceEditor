using JustDanceEditor.Formats.JDI.Video;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

internal static class PcmPlaybackEngineFactory
{
    public static IPcmPlaybackEngine Create()
    {
        if (!OperatingSystem.IsLinux())
            return new PortAudioPcmPlaybackEngine();

        string? ffplayPath = JdiFfmpegResolver.TryGetFfplayPath();
        if (LinuxAudioEnvironment.IsWsl())
        {
            if (PulseAudioPcmPlaybackEngine.IsAvailable())
            {
                IPcmPlaybackEngine pulseAudio = new PulseAudioPcmPlaybackEngine();
                return ffplayPath != null
                    ? new ResilientPcmPlaybackEngine(pulseAudio, new FfplayPcmPlaybackEngine(ffplayPath))
                    : pulseAudio;
            }

            return ffplayPath != null ? new FfplayPcmPlaybackEngine(ffplayPath) : new SilentPcmPlaybackEngine();
        }

        IPcmPlaybackEngine portAudio = new PortAudioPcmPlaybackEngine();
        return ffplayPath != null
            ? new ResilientPcmPlaybackEngine(portAudio, new FfplayPcmPlaybackEngine(ffplayPath))
            : portAudio;
    }
}

internal sealed class ResilientPcmPlaybackEngine(IPcmPlaybackEngine primary, IPcmPlaybackEngine fallback) : IPcmPlaybackEngine, IAudioClockPlaybackEngine
{
    private readonly IPcmPlaybackEngine _primary = primary;
    private readonly IPcmPlaybackEngine _fallback = fallback;
    private IPcmPlaybackEngine _active = primary;

    private PcmWaveAudioData? _audio;
    private bool _fallbackLoaded;
    private bool _disposed;
    private bool _isMetronomeEnabled;
    private double _zeroBeatTimeSeconds;
    private double _bpm = 120;
    private int _beatsPerMeasure = 4;
    private IEnumerable<double>? _sectionStarts;

    public event EventHandler? PlaybackCompleted
    {
        add
        {
            _primary.PlaybackCompleted += value;
            _fallback.PlaybackCompleted += value;
        }
        remove
        {
            _primary.PlaybackCompleted -= value;
            _fallback.PlaybackCompleted -= value;
        }
    }

    public bool IsLoaded => _active.IsLoaded;
    public TimeSpan Duration => _active.Duration;

    public bool TryGetPlaybackTime(out TimeSpan time)
    {
        if (_active is IAudioClockPlaybackEngine clock && clock.TryGetPlaybackTime(out time))
            return true;

        time = default;
        return false;
    }

    public bool IsMetronomeEnabled
    {
        get => _isMetronomeEnabled;
        set
        {
            _isMetronomeEnabled = value;
            _active.IsMetronomeEnabled = value;
        }
    }

    public void Load(PcmWaveAudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        _audio = audio;
        _fallbackLoaded = false;
        _active = _primary;
        _primary.Load(audio);
        ApplyState(_primary);
    }

    public void Play(TimeSpan startTime, bool completeAtAudioEnd)
    {
        ThrowIfDisposed();

        try
        {
            _active.Play(startTime, completeAtAudioEnd);
        }
        catch (Exception primaryException) when (!ReferenceEquals(_active, _fallback))
        {
            SwitchToFallback(primaryException);
            try
            {
                _active.Play(startTime, completeAtAudioEnd);
            }
            catch (Exception fallbackException)
            {
                throw new AudioPlaybackUnavailableException(
                    "Audio playback is unavailable.",
                    new AggregateException(primaryException, fallbackException));
            }
        }
    }

    public void Pause()
    {
        _active.Pause();
    }

    public void Stop()
    {
        _active.Stop();
    }

    public void Seek(TimeSpan position)
    {
        _active.Seek(position);
    }

    public void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null)
    {
        _zeroBeatTimeSeconds = zeroBeatTimeSeconds;
        _bpm = bpm;
        _beatsPerMeasure = beatsPerMeasure;
        _sectionStarts = sectionStarts;
        _active.UpdateMetronome(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts);
    }

    private void SwitchToFallback(Exception cause)
    {
        if (_audio == null)
            throw new AudioPlaybackUnavailableException("Audio playback is unavailable.", cause);

        _primary.Dispose();
        _active = _fallback;

        if (!_fallbackLoaded)
        {
            _fallback.Load(_audio);
            _fallbackLoaded = true;
        }

        ApplyState(_fallback);
    }

    private void ApplyState(IPcmPlaybackEngine engine)
    {
        engine.IsMetronomeEnabled = _isMetronomeEnabled;
        engine.UpdateMetronome(_zeroBeatTimeSeconds, _bpm, _beatsPerMeasure, _sectionStarts);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _primary.Dispose();
        _fallback.Dispose();
        _disposed = true;
    }
}
