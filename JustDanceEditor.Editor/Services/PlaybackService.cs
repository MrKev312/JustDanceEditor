using Avalonia.Threading;

using KevInc.Audio.NAudio.Providers;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public class PlaybackService : IPlaybackService, IDisposable
{
    private readonly Func<IPcmPlaybackEngine> _pcmPlaybackEngineFactory;
    private readonly bool _useWindowsAudio;
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _audioFile;
    private EndlessSampleProvider? _endless;
    private MetronomeSampleProvider? _metronome;
    private IPcmPlaybackEngine? _pcmPlayer;

    private Func<double, double> _beatToSeconds = b => b * 0.5;
    private Func<double, double> _secondsToBeat = s => s / 0.5;

    private TimeSpan _baseTime = TimeSpan.Zero;
    private readonly DispatcherTimer _updateTimer;
    private readonly Stopwatch _stopwatch = new();

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
            TimeSpan audioDur = _audioFile?.TotalTime ?? TimeSpan.Zero;
            audioDur = _pcmPlayer?.Duration ?? audioDur;
            return field > audioDur ? field : audioDur;
        }

        private set;
    } = TimeSpan.Zero;

    public event EventHandler? TimeChanged;
    public event EventHandler? PlayStateChanged;

    public PlaybackService()
        : this(PcmPlaybackEngineFactory.Create, OperatingSystem.IsWindows())
    {
    }

    internal PlaybackService(Func<IPcmPlaybackEngine> pcmPlaybackEngineFactory, bool useWindowsAudio)
    {
        _pcmPlaybackEngineFactory = pcmPlaybackEngineFactory;
        _useWindowsAudio = useWindowsAudio;
        _updateTimer = new DispatcherTimer(
            DisplayRefreshRateProvider.GetRefreshInterval(),
            DispatcherPriority.Render,
            (s, e) =>
            {
                if (IsPlaying)
                {
                    if (CurrentTime >= Duration)
                    {
                        Pause();
                        Seek(Duration);
                    }

                    TimeChanged?.Invoke(this, EventArgs.Empty);
                }
            });
    }

    public async Task LoadMediaAsync(
        string audioPath,
        Func<double, double> beatToSeconds,
        Func<double, double> secondsToBeat)
    {
        Pause();

        _beatToSeconds = beatToSeconds;
        _secondsToBeat = secondsToBeat;

        CleanUpAudio();

        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            if (_useWindowsAudio)
            {
                await Task.Run(() =>
                {
                    _audioFile = new AudioFileReader(audioPath);
                    _endless = new EndlessSampleProvider(_audioFile, _audioFile.TotalTime);
                    _metronome = new MetronomeSampleProvider(_endless);
                    _outputDevice = new WasapiOut(AudioClientShareMode.Shared, 10);
                    _outputDevice.Init(_metronome);
                });
            }
            else
            {
                await Task.Run(() =>
                {
                    _pcmPlayer = _pcmPlaybackEngineFactory();
                    _pcmPlayer.Load(audioPath);
                });
            }
        }

        // Reset timing
        _baseTime = TimeSpan.Zero;
        _stopwatch.Reset();
        UpdateTimerInterval();
        _updateTimer.Start();

        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (IsPlaying)
            return;

        if (CurrentTime >= Duration)
        {
            Seek(TimeSpan.Zero);
        }

        UpdateTimerInterval();
        TimeSpan startTime = CurrentTime;

        if (_audioFile != null)
        {
            TimeSpan audioPos = startTime < _audioFile.TotalTime ? startTime : _audioFile.TotalTime;
            _audioFile.CurrentTime = audioPos;
        }

        _endless?.Reset(startTime.TotalSeconds);
        _metronome?.ResetPosition(startTime.TotalSeconds);
        TryPlayPcm(startTime);

        IsPlaying = true;
        _stopwatch.Restart();
        TryPlayOutputDevice();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        _baseTime = CurrentTime;
        _stopwatch.Stop();
        IsPlaying = false;

        TryPauseOutputDevice();
        TryPausePcm();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan time)
    {
        bool wasPlaying = IsPlaying;
        if (wasPlaying)
            Pause();

        _baseTime = time;
        if (_baseTime < TimeSpan.Zero)
            _baseTime = TimeSpan.Zero;
        if (_baseTime > Duration)
            _baseTime = Duration;

        // Clamp audio file reader to its own duration (CurrentTime can't go past TotalTime)
        if (_audioFile != null)
        {
            TimeSpan audioPos = _baseTime < _audioFile.TotalTime ? _baseTime : _audioFile.TotalTime;
            _audioFile.CurrentTime = audioPos;
        }

        _endless?.Reset(_baseTime.TotalSeconds);
        _metronome?.ResetPosition(_baseTime.TotalSeconds);
        TrySeekPcm(_baseTime);

        TimeChanged?.Invoke(this, EventArgs.Empty);

        if (wasPlaying)
            Play();
    }

    public void SeekToBeat(double beat)
    {
        double seconds = _beatToSeconds(beat);
        Seek(TimeSpan.FromSeconds(seconds));
    }

    private void CleanUpAudio()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _audioFile?.Dispose();
        _pcmPlayer?.Dispose();

        _outputDevice = null;
        _audioFile = null;
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

    public void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null)
    {
        _metronome?.UpdateTiming(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts);
        _pcmPlayer?.UpdateMetronome(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts);
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
        Debug.WriteLine($"PCM audio playback disabled: {ex}");
        _pcmPlayer?.Dispose();
        _pcmPlayer = null;
    }

    private void DisableWaveOutPlayback(Exception ex)
    {
        Debug.WriteLine($"Wave audio playback disabled: {ex}");
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
        _updateTimer.Interval = DisplayRefreshRateProvider.GetRefreshInterval();
    }

    public void Dispose()
    {
        _updateTimer.Stop();
        _stopwatch.Stop();

        CleanUpAudio();

        GC.SuppressFinalize(this);
    }
}
