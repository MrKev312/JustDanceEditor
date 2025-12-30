using Avalonia.Threading;

using LibVLCSharp.Shared;

using NAudio.Wave;

using Xabe.FFmpeg;

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

    private readonly DispatcherTimer _updateTimer;
    private readonly Stopwatch _stopwatch = new();
    private bool _isPlaying;

    public MediaPlayer? MediaPlayer => null; // Tools now manage their own video
    public bool IsPlaying => _isPlaying;

    public TimeSpan CurrentTime => _stopwatch.Elapsed;

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
                if (_isPlaying)
                {
                    if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime)
                    {
                        Pause();
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
        _stopwatch.Reset();
        _updateTimer.Start();

        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (_isPlaying)
            return;

        _isPlaying = true;
        _stopwatch.Start();

        if (_audioFile != null)
        {
            if (_audioFile.CurrentTime >= _audioFile.TotalTime)
            {
                _stopwatch.Restart();
                _audioFile.CurrentTime = TimeSpan.Zero;
            }
            else if (Math.Abs(_audioFile.CurrentTime.TotalSeconds - _stopwatch.Elapsed.TotalSeconds) > 0.1)
            {
                _audioFile.CurrentTime = _stopwatch.Elapsed;
            }
        }

        _outputDevice?.Play();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (!_isPlaying)
            return;

        _stopwatch.Stop();
        _isPlaying = false;

        _outputDevice?.Pause();

        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan time)
    {
        bool wasPlaying = _isPlaying;

        if (wasPlaying)
            _stopwatch.Restart();
        else
            _stopwatch.Reset();

        if (_audioFile != null)
            _audioFile.CurrentTime = time;

        TimeChanged?.Invoke(this, EventArgs.Empty);
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
    }
}
