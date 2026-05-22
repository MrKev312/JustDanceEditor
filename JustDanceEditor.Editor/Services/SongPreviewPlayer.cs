using JustDanceEditor.Editor.ViewModels.Dialogs;

using KevInc.Audio.NAudio.Providers;

using NAudio.Wave;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Manages audio playback with metronome for the New Song dialog.
/// Converts the source audio to WAV via FFmpeg, then plays through the
/// platform audio backend with beat/section click sounds.
/// </summary>
public class SongPreviewPlayer : IDisposable
{
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioReader;
    private MetronomeSampleProvider? _metronome;
    private PortAudioPcmPlaybackEngine? _portAudioPlayer;
    private string? _tempWavPath;
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _baseTime = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _disposed;

    public bool IsPlaying { get; private set; }
    public bool IsLoaded => _duration > TimeSpan.Zero;
    public TimeSpan Duration => _audioReader?.TotalTime ?? _duration;

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

    /// <summary>
    /// Loads the audio file for preview playback.
    /// Converts to WAV via FFmpeg first (to support Opus and other formats).
    /// </summary>
    public async Task LoadAsync(string audioFilePath)
    {
        Stop();
        CleanUp();

        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            return;

        // Convert to WAV via FFmpeg
        _tempWavPath = await AudioConversionService.ConvertToWavAsync(audioFilePath);
        _duration = TimeSpan.FromSeconds(await AudioConversionService.GetDurationAsync(_tempWavPath));

        if (!OperatingSystem.IsWindows())
        {
            _portAudioPlayer = new PortAudioPcmPlaybackEngine();
            _portAudioPlayer.Load(_tempWavPath);
            _portAudioPlayer.PlaybackCompleted += OnPortAudioPlaybackCompleted;
            _duration = _portAudioPlayer.Duration;
            _baseTime = TimeSpan.Zero;
            _stopwatch.Reset();
            return;
        }

        _audioReader = new AudioFileReader(_tempWavPath);
        _duration = _audioReader.TotalTime;
        _metronome = new MetronomeSampleProvider(_audioReader);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_metronome);
        _outputDevice.PlaybackStopped += OnPlaybackStopped;

        _baseTime = TimeSpan.Zero;
        _stopwatch.Reset();
    }

    /// <summary>
    /// Updates the metronome timing parameters. Safe to call during playback.
    /// Always enables the metronome since SongPreviewPlayer is used for audible beat feedback.
    /// </summary>
    public void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<SectionEntry>? sections = null)
    {
        if (_metronome != null)
        {
            _metronome.Enabled = true;
            _metronome.UpdateTiming(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sections?.Select(s => s.StartBeat));
        }

        if (_portAudioPlayer != null)
        {
            _portAudioPlayer.IsMetronomeEnabled = true;
            _portAudioPlayer.UpdateMetronome(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sections?.Select(s => (double)s.StartBeat));
        }
    }

    public void Play()
    {
        if (!IsLoaded || IsPlaying)
            return;

        if (CurrentTime >= Duration)
            Seek(TimeSpan.Zero);

        if (_audioReader != null)
            _audioReader.CurrentTime = CurrentTime;

        _metronome?.ResetPosition(CurrentTime.TotalSeconds);
        _portAudioPlayer?.Play(CurrentTime, completeAtAudioEnd: true);
        _stopwatch.Restart();
        _outputDevice?.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        _outputDevice?.Pause();
        _portAudioPlayer?.Pause();
        _baseTime += _stopwatch.Elapsed;
        _stopwatch.Stop();
        IsPlaying = false;
    }

    public void Stop()
    {
        if (!IsLoaded && !IsPlaying)
            return;

        _outputDevice?.Stop();
        _portAudioPlayer?.Stop();
        _baseTime = TimeSpan.Zero;
        _stopwatch.Reset();
        IsPlaying = false;

        _audioReader?.Position = 0;

        _metronome?.ResetPosition(0);
    }

    public void Seek(TimeSpan time)
    {
        if (!IsLoaded)
            return;

        time = TimeSpan.FromSeconds(Math.Clamp(time.TotalSeconds, 0, Duration.TotalSeconds));
        _baseTime = time;
        _stopwatch.Reset();

        if (IsPlaying)
        {
            if (_audioReader != null)
                _audioReader.CurrentTime = time;

            _metronome?.ResetPosition(time.TotalSeconds);
            _portAudioPlayer?.Seek(time);
            _stopwatch.Restart();
        }
        else
        {
            _portAudioPlayer?.Seek(time);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (IsPlaying)
        {
            _baseTime += _stopwatch.Elapsed;
            _stopwatch.Stop();
            IsPlaying = false;
        }
    }

    private void OnPortAudioPlaybackCompleted(object? sender, EventArgs e)
    {
        if (!IsPlaying)
            return;

        _baseTime = Duration;
        _stopwatch.Stop();
        IsPlaying = false;
    }

    private void CleanUp()
    {
        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _audioReader?.Dispose();
        _audioReader = null;
        _metronome = null;
        if (_portAudioPlayer != null)
            _portAudioPlayer.PlaybackCompleted -= OnPortAudioPlaybackCompleted;
        _portAudioPlayer?.Dispose();
        _portAudioPlayer = null;
        _duration = TimeSpan.Zero;

        if (_tempWavPath != null)
        {
            try
            {
                File.Delete(_tempWavPath);
            }
            catch { /* ignore */ }

            _tempWavPath = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        CleanUp();
        GC.SuppressFinalize(this);
    }
}
