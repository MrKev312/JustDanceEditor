using Avalonia.Controls;
using Avalonia.Threading;

using KevInc.Audio.NAudio.Providers;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public class PlaybackService : IPlaybackService, IDisposable
{
    private readonly Func<IPcmPlaybackEngine> _pcmPlaybackEngineFactory;
    private readonly bool _useWindowsAudio;
    private readonly IWindowService? _windows;
    private IWavePlayer? _outputDevice;
    private PcmWaveSampleProvider? _audioSource;
    private EndlessSampleProvider? _endless;
    private VariableMetronomeSampleProvider? _metronome;
    private IPcmPlaybackEngine? _pcmPlayer;

    private Func<double, double> _beatToSeconds = b => b * 0.5;
    private Func<double, double> _secondsToBeat = s => s / 0.5;

    private TimeSpan _baseTime = TimeSpan.Zero;
    private readonly DispatcherTimer _fallbackUpdateTimer;
    private readonly Stopwatch _stopwatch = new();
    private int _animationFrameRequestId;
    private bool _animationFramePending;
    private bool _disposed;

    public bool IsPlaying { get; private set; }

    public TimeSpan CurrentTime
    {
        get
        {
            if (IsPlaying && TryGetPcmPlaybackTime(out TimeSpan playbackTime))
                return ClampToDuration(playbackTime);

            TimeSpan time = _baseTime;
            if (IsPlaying)
                time += _stopwatch.Elapsed;
            return ClampToDuration(time);
        }
    }

    public double CurrentBeat
        => _secondsToBeat(CurrentTime.TotalSeconds);

    public TimeSpan Duration
    {
        get
        {
            TimeSpan audioDur = _audioSource?.TotalTime ?? TimeSpan.Zero;
            audioDur = _pcmPlayer?.Duration ?? audioDur;
            return field > audioDur ? field : audioDur;
        }

        private set;
    } = TimeSpan.Zero;

    public event EventHandler? TimeChanged;
    public event EventHandler? PlayStateChanged;
    public event EventHandler<PlaybackInteractionBlockedEventArgs>? InteractionBlocked;

    public bool IsInteractionLocked { get; set; }

    public PlaybackService()
        : this(PcmPlaybackEngineFactory.Create, OperatingSystem.IsWindows(), null)
    {
    }

    public PlaybackService(IWindowService windows)
        : this(PcmPlaybackEngineFactory.Create, OperatingSystem.IsWindows(), windows)
    {
    }

    internal PlaybackService(Func<IPcmPlaybackEngine> pcmPlaybackEngineFactory, bool useWindowsAudio)
        : this(pcmPlaybackEngineFactory, useWindowsAudio, null)
    {
    }

    private PlaybackService(
        Func<IPcmPlaybackEngine> pcmPlaybackEngineFactory,
        bool useWindowsAudio,
        IWindowService? windows)
    {
        _pcmPlaybackEngineFactory = pcmPlaybackEngineFactory;
        _useWindowsAudio = useWindowsAudio;
        _windows = windows;
        _fallbackUpdateTimer = new DispatcherTimer(
            DisplayRefreshRateProvider.GetRefreshInterval(windows?.MainWindow),
            DispatcherPriority.Render,
            (s, e) => OnPlaybackTick());
    }

    public async Task LoadMediaAsync(
        PcmWaveAudioData? audio,
        Func<double, double> beatToSeconds,
        Func<double, double> secondsToBeat)
    {
        PauseCore();

        _beatToSeconds = beatToSeconds;
        _secondsToBeat = secondsToBeat;

        CleanUpAudio();

        if (audio != null)
        {
            if (_useWindowsAudio)
            {
                await Task.Run(() =>
                {
                    _audioSource = new PcmWaveSampleProvider(audio);
                    _endless = new EndlessSampleProvider(_audioSource, _audioSource.TotalTime);
                    _metronome = new VariableMetronomeSampleProvider(_endless);
                    _outputDevice = new WasapiOut(AudioClientShareMode.Shared, 10);
                    _outputDevice.Init(_metronome);
                });
            }
            else
            {
                await Task.Run(() =>
                {
                    _pcmPlayer = _pcmPlaybackEngineFactory();
                    _pcmPlayer.Load(audio);
                });
            }
        }

        // Reset timing
        _baseTime = TimeSpan.Zero;
        _stopwatch.Reset();
        UpdateTimerInterval();

        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (IsPlaying)
            return;

        if (CurrentTime >= Duration)
        {
            SeekCore(TimeSpan.Zero, restorePlayback: false);
        }

        UpdateTimerInterval();
        TimeSpan startTime = CurrentTime;

        if (_audioSource != null)
        {
            TimeSpan audioPos = startTime < _audioSource.TotalTime ? startTime : _audioSource.TotalTime;
            _audioSource.CurrentTime = audioPos;
        }

        _endless?.Reset(startTime.TotalSeconds);
        _metronome?.ResetPosition(startTime.TotalSeconds);
        TryPlayPcm(startTime);

        IsPlaying = true;
        _stopwatch.Restart();
        TryPlayOutputDevice();
        StartPlaybackUpdates();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (TryBlockInteraction(PlaybackInteractionKind.Pause))
            return;

        PauseCore();
    }

    private void PauseCore()
    {
        if (!IsPlaying)
            return;

        _baseTime = CurrentTime;
        _stopwatch.Stop();
        IsPlaying = false;
        StopPlaybackUpdates();

        TryPauseOutputDevice();
        TryPausePcm();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan time)
    {
        if (TryBlockInteraction(PlaybackInteractionKind.Seek, time))
            return;

        SeekCore(time, restorePlayback: true);
    }

    private void SeekCore(TimeSpan time, bool restorePlayback)
    {
        bool wasPlaying = IsPlaying;
        if (wasPlaying)
            PauseCore();

        _baseTime = time;
        if (_baseTime < TimeSpan.Zero)
            _baseTime = TimeSpan.Zero;
        if (_baseTime > Duration)
            _baseTime = Duration;

        // Clamp audio file reader to its own duration (CurrentTime can't go past TotalTime)
        if (_audioSource != null)
        {
            TimeSpan audioPos = _baseTime < _audioSource.TotalTime ? _baseTime : _audioSource.TotalTime;
            _audioSource.CurrentTime = audioPos;
        }

        _endless?.Reset(_baseTime.TotalSeconds);
        _metronome?.ResetPosition(_baseTime.TotalSeconds);
        TrySeekPcm(_baseTime);

        TimeChanged?.Invoke(this, EventArgs.Empty);

        if (wasPlaying && restorePlayback)
            Play();
    }

    public void SeekToBeat(double beat)
    {
        double seconds = _beatToSeconds(beat);
        Seek(TimeSpan.FromSeconds(seconds));
    }

    private bool TryBlockInteraction(PlaybackInteractionKind kind, TimeSpan? targetTime = null)
    {
        if (!IsInteractionLocked)
            return false;

        InteractionBlocked?.Invoke(this, new PlaybackInteractionBlockedEventArgs(kind, targetTime));
        return true;
    }

    private void CleanUpAudio()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _pcmPlayer?.Dispose();

        _outputDevice = null;
        _audioSource = null;
        _endless = null;
        _metronome = null;
        _pcmPlayer = null;
        Duration = TimeSpan.Zero;
    }

    public void SetExtendedEnd(TimeSpan end)
    {
        Duration = end;
    }

    public bool IsMetronomeEnabled
    {
        get => _metronome?.Enabled ?? _pcmPlayer?.IsMetronomeEnabled ?? false;
        set
        {
            _metronome?.Enabled = value;
            if (_pcmPlayer != null)
                _pcmPlayer.IsMetronomeEnabled = value;
        }
    }

    public void UpdateMetronome(
        double zeroBeatTimeSeconds,
        double bpm,
        int beatsPerMeasure,
        IEnumerable<double>? sectionStarts = null)
    {
        _metronome?.UpdateTiming(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts, _beatToSeconds, _secondsToBeat);
        _pcmPlayer?.UpdateMetronome(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts, _beatToSeconds, _secondsToBeat);
    }

    private void TryPlayPcm(TimeSpan startTime)
    {
        if (_pcmPlayer == null)
            return;

        try
        {
            _pcmPlayer.Play(startTime, completeAtAudioEnd: false);
        }
        catch (Exception ex)
        {
            DisablePcmPlayback(ex);
        }
    }

    private void TryPausePcm()
    {
        try
        {
            _pcmPlayer?.Pause();
        }
        catch (Exception ex)
        {
            DisablePcmPlayback(ex);
        }
    }

    private void TrySeekPcm(TimeSpan time)
    {
        try
        {
            _pcmPlayer?.Seek(time);
        }
        catch (Exception ex)
        {
            DisablePcmPlayback(ex);
        }
    }

    private void TryPlayOutputDevice()
    {
        try
        {
            _outputDevice?.Play();
        }
        catch (Exception ex)
        {
            DisableWaveOutPlayback(ex);
        }
    }

    private void TryPauseOutputDevice()
    {
        try
        {
            _outputDevice?.Pause();
        }
        catch (Exception ex)
        {
            DisableWaveOutPlayback(ex);
        }
    }

    private void DisablePcmPlayback(Exception ex)
    {
        EditorLog.Unexpected(ex, "PCM audio playback");
        _pcmPlayer?.Dispose();
        _pcmPlayer = null;
    }

    private void DisableWaveOutPlayback(Exception ex)
    {
        EditorLog.Unexpected(ex, "Wave audio playback");
        _outputDevice?.Dispose();
        _outputDevice = null;
    }

    private bool TryGetPcmPlaybackTime(out TimeSpan playbackTime)
    {
        if (_pcmPlayer is IAudioClockPlaybackEngine clock && clock.TryGetPlaybackTime(out playbackTime))
            return true;

        playbackTime = default;
        return false;
    }

    private TimeSpan ClampToDuration(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            return TimeSpan.Zero;

        TimeSpan duration = Duration;
        return duration > TimeSpan.Zero && time > duration ? duration : time;
    }

    private void UpdateTimerInterval()
    {
        _fallbackUpdateTimer.Interval = DisplayRefreshRateProvider.GetRefreshInterval(_windows?.MainWindow);
    }

    private void StartPlaybackUpdates()
    {
        if (_disposed || !IsPlaying)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(StartPlaybackUpdates, DispatcherPriority.Render);
            return;
        }

        if (TryRequestAnimationFrame())
        {
            _fallbackUpdateTimer.Stop();
            return;
        }

        UpdateTimerInterval();
        _fallbackUpdateTimer.Start();
    }

    private void StopPlaybackUpdates()
    {
        _fallbackUpdateTimer.Stop();
        _animationFramePending = false;
        _animationFrameRequestId++;
    }

    private bool TryRequestAnimationFrame()
    {
        if (_animationFramePending)
            return true;

        TopLevel? topLevel = _windows?.MainWindow;
        if (topLevel == null)
            return false;

        _animationFramePending = true;
        int requestId = ++_animationFrameRequestId;
        topLevel.RequestAnimationFrame(_ => OnAnimationFrame(requestId));
        return true;
    }

    private void OnAnimationFrame(int requestId)
    {
        if (requestId != _animationFrameRequestId)
            return;

        _animationFramePending = false;
        if (_disposed || !IsPlaying)
            return;

        OnPlaybackTick();

        if (IsPlaying)
            StartPlaybackUpdates();
    }

    private void OnPlaybackTick()
    {
        if (_disposed || !IsPlaying)
            return;

        if (CurrentTime >= Duration)
        {
            PauseCore();
            SeekCore(Duration, restorePlayback: false);
            return;
        }

        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _disposed = true;
        StopPlaybackUpdates();
        _stopwatch.Stop();

        CleanUpAudio();

        GC.SuppressFinalize(this);
    }
}
