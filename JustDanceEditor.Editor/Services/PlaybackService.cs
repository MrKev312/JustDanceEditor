using Avalonia.Threading;

using LibVLCSharp.Shared;

using NAudio.Wave;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public class PlaybackService : IPlaybackService, IDisposable
{
    // NAudio (audio only)
    private IWavePlayer? _outputDevice;
    private WaveStream? _audioFile;

    private Func<double, double> _beatToSeconds = b => b * 0.5;
    private Func<double, double> _secondsToBeat = s => s / 0.5;
    private double _videoStartOffset = 0;

    private TimeSpan _baseTime = TimeSpan.Zero;
    private readonly DispatcherTimer _updateTimer;
    private readonly Stopwatch _stopwatch = new();

    public MediaPlayer? MediaPlayer => null; // Tools now manage their own video
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

    public TimeSpan Duration => _audioFile?.TotalTime ?? TimeSpan.Zero;

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
        string videoPath,
        Func<double, double> beatToSeconds,
        Func<double, double> secondsToBeat,
        double videoStartOffset)
    {
        Pause();

        _beatToSeconds = beatToSeconds;
        _secondsToBeat = secondsToBeat;
        _videoStartOffset = videoStartOffset;

        // ---------- AUDIO (NAudio) ----------
        CleanUpAudio();

        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            await Task.Run(() => {
                // Load WAV with NAudio (reliable)
                _audioFile = new AudioFileReader(audioPath);
                _outputDevice = new WaveOutEvent();
                _outputDevice.Init(_audioFile);
            });
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

        IsPlaying = true;
        _stopwatch.Restart();

        _audioFile?.CurrentTime = CurrentTime;

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
        if (_audioFile != null && _baseTime > _audioFile.TotalTime)
            _baseTime = _audioFile.TotalTime;

        _audioFile?.CurrentTime = _baseTime;

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

        _outputDevice = null;
        _audioFile = null;
    }

    public void Dispose()
    {
        _updateTimer.Stop();
        _stopwatch.Stop();

        CleanUpAudio();

        GC.SuppressFinalize(this);
    }
}
