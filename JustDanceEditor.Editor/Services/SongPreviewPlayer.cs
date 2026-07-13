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
/// Decodes source audio through FFmpeg, then plays through the platform audio
/// backend with beat/section click sounds.
/// </summary>
public class SongPreviewPlayer : IDisposable
{
    private WaveOutEvent? _outputDevice;
    private PcmWaveSampleProvider? _audioSource;
    private MetronomeSampleProvider? _metronome;
    private IPcmPlaybackEngine? _pcmPlayer;
    private PcmWaveAudioData? _audio;
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _baseTime = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _disposed;

    public bool IsPlaying { get; private set; }
    public bool IsLoaded => _duration > TimeSpan.Zero;
    public TimeSpan Duration => _audioSource?.TotalTime ?? _duration;

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
    /// Decodes via FFmpeg first to support Opus and other formats.
    /// </summary>
    public async Task LoadAsync(string audioFilePath)
    {
        Stop();
        CleanUp();

        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            return;

        _audio = await AudioConversionService.DecodeToPcmAsync(audioFilePath);
        _duration = _audio.Duration;

        if (!OperatingSystem.IsWindows())
        {
            _pcmPlayer = PcmPlaybackEngineFactory.Create();
            _pcmPlayer.Load(_audio);
            _pcmPlayer.PlaybackCompleted += OnPcmPlaybackCompleted;
            _duration = _pcmPlayer.Duration;
            _baseTime = TimeSpan.Zero;
            _stopwatch.Reset();
            return;
        }

        _audioSource = new PcmWaveSampleProvider(_audio);
        _duration = _audioSource.TotalTime;
        _metronome = new MetronomeSampleProvider(_audioSource);
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

        if (_pcmPlayer != null)
        {
            _pcmPlayer.IsMetronomeEnabled = true;
            _pcmPlayer.UpdateMetronome(zeroBeatTimeSeconds, bpm, beatsPerMeasure, sections?.Select(s => (double)s.StartBeat));
        }
    }

    public void Play()
    {
        if (!IsLoaded || IsPlaying)
            return;

        if (CurrentTime >= Duration)
            Seek(TimeSpan.Zero);

        if (_audioSource != null)
            _audioSource.CurrentTime = CurrentTime;

        if (_outputDevice == null && _pcmPlayer == null)
            return;

        _metronome?.ResetPosition(CurrentTime.TotalSeconds);
        try
        {
            _pcmPlayer?.Play(CurrentTime, completeAtAudioEnd: true);
            _outputDevice?.Play();
            _stopwatch.Restart();
            IsPlaying = true;
        }
        catch (Exception ex)
        {
            DisablePlayback(ex);
        }
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        try
        {
            _outputDevice?.Pause();
            _pcmPlayer?.Pause();
        }
        catch (Exception ex)
        {
            DisablePlayback(ex);
        }

        _baseTime += _stopwatch.Elapsed;
        _stopwatch.Stop();
        IsPlaying = false;
    }

    public void Stop()
    {
        if (!IsLoaded && !IsPlaying)
            return;

        try
        {
            _outputDevice?.Stop();
            _pcmPlayer?.Stop();
        }
        catch (Exception ex)
        {
            DisablePlayback(ex);
        }

        _baseTime = TimeSpan.Zero;
        _stopwatch.Reset();
        IsPlaying = false;

        if (_audioSource != null)
            _audioSource.CurrentTime = TimeSpan.Zero;
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
            if (_audioSource != null)
                _audioSource.CurrentTime = time;

            _metronome?.ResetPosition(time.TotalSeconds);
            try
            {
                _pcmPlayer?.Seek(time);
            }
            catch (Exception ex)
            {
                DisablePlayback(ex);
            }

            _stopwatch.Restart();
        }
        else
        {
            try
            {
                _pcmPlayer?.Seek(time);
            }
            catch (Exception ex)
            {
                DisablePlayback(ex);
            }
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

    private void OnPcmPlaybackCompleted(object? sender, EventArgs e)
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

        _audioSource = null;
        _metronome = null;
        if (_pcmPlayer != null)
            _pcmPlayer.PlaybackCompleted -= OnPcmPlaybackCompleted;
        _pcmPlayer?.Dispose();
        _pcmPlayer = null;
        _audio = null;
        _duration = TimeSpan.Zero;
    }

    private void DisablePlayback(Exception ex)
    {
        EditorLog.Unexpected(ex, "Song preview playback");

        if (_pcmPlayer != null)
            _pcmPlayer.PlaybackCompleted -= OnPcmPlaybackCompleted;

        try
        {
            _outputDevice?.Dispose();
            _pcmPlayer?.Dispose();
        }
        catch (Exception disposeException)
        {
            EditorLog.Unexpected(disposeException, "Dispose disabled song preview playback");
        }

        _outputDevice = null;
        _pcmPlayer = null;
        _stopwatch.Stop();
        IsPlaying = false;
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