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
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _audioFile;
    private EndlessSampleProvider? _endless;
    private MetronomeSampleProvider? _metronome;
    private PortAudioPcmPlaybackEngine? _portAudioPlayer;

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
            TimeSpan time = _baseTime;
            if (IsPlaying)
                time += _stopwatch.Elapsed;
            return time;
        }
    }

    public double CurrentBeat
        => _secondsToBeat(CurrentTime.TotalSeconds);

    public TimeSpan Duration
    {
        get
        {
            TimeSpan audioDur = _audioFile?.TotalTime ?? TimeSpan.Zero;
            audioDur = _portAudioPlayer?.Duration ?? audioDur;
            return field > audioDur ? field : audioDur;
        }

        private set;
    } = TimeSpan.Zero;

    public event EventHandler? TimeChanged;
    public event EventHandler? PlayStateChanged;

    public PlaybackService()
    {
        _updateTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
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
            if (OperatingSystem.IsWindows())
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
                    _portAudioPlayer = new PortAudioPcmPlaybackEngine();
                    _portAudioPlayer.Load(audioPath);
                });
            }
        }

        // Reset timing
        _baseTime = TimeSpan.Zero;
        _stopwatch.Reset();
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

        TimeSpan startTime = CurrentTime;

        if (_audioFile != null)
        {
            TimeSpan audioPos = startTime < _audioFile.TotalTime ? startTime : _audioFile.TotalTime;
            _audioFile.CurrentTime = audioPos;
        }

        _endless?.Reset(startTime.TotalSeconds);
        _metronome?.ResetPosition(startTime.TotalSeconds);
        _portAudioPlayer?.Play(startTime, completeAtAudioEnd: false);

        IsPlaying = true;
        _stopwatch.Restart();
        _outputDevice?.Play();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        _baseTime += _stopwatch.Elapsed;
        _stopwatch.Stop();
        IsPlaying = false;

        _outputDevice?.Pause();
        _portAudioPlayer?.Pause();

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
        _portAudioPlayer?.Seek(_baseTime);

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
        _portAudioPlayer?.Dispose();

        _outputDevice = null;
        _audioFile = null;
        _endless = null;
        _metronome = null;
        _portAudioPlayer = null;
        Duration = TimeSpan.Zero;
    }

    public void SetExtendedEnd(TimeSpan end)
    {
        Duration = end;
    }

    public bool IsMetronomeEnabled
    {
        get => _metronome?.Enabled ?? _portAudioPlayer?.IsMetronomeEnabled ?? false;
        set
        {
            _metronome?.Enabled = value;
            if (_portAudioPlayer != null)
                _portAudioPlayer.IsMetronomeEnabled = value;
        }
    }

    public void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null)
    {
        _metronome?.UpdateTiming(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts);
        _portAudioPlayer?.UpdateMetronome(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sectionStarts);
    }

    public void Dispose()
    {
        _updateTimer.Stop();
        _stopwatch.Stop();

        CleanUpAudio();

        GC.SuppressFinalize(this);
    }
}
