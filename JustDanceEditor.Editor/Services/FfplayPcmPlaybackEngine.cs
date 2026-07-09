using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

internal sealed class FfplayPcmPlaybackEngine(string ffplayPath) : IPcmPlaybackEngine
{
    private const int FramesPerBuffer = 512;
    private const double InitialPrebufferSeconds = 0.18;
    private const double ClickDurationSeconds = 0.035;

    private sealed class PlaybackProcess(
        Process process,
        CancellationTokenSource cancellation,
        bool completeAtAudioEnd)
    {
        public Process Process { get; } = process;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public bool CompleteAtAudioEnd { get; } = completeAtAudioEnd;
        public bool StopRequested { get; set; }
        public short[] SampleBuffer { get; set; } = [];
        public byte[] ByteBuffer { get; set; } = [];
    }

    private readonly Lock _gate = new();
    private readonly string _ffplayPath = ffplayPath;

    private PcmWaveAudioData? _audio;
    private PlaybackProcess? _playback;
    private long _positionFrames;
    private bool _completeAtAudioEnd;
    private bool _isPlaying;
    private bool _disposed;
    private double _zeroBeatTimeSeconds;
    private double _bpm = 120;
    private int _beatsPerMeasure = 4;
    private HashSet<int> _sectionStartBeats = [];

    public event EventHandler? PlaybackCompleted;

    public bool IsLoaded => _audio != null;
    public TimeSpan Duration => _audio?.Duration ?? TimeSpan.Zero;

    public bool IsMetronomeEnabled { get; set; }

    public void Load(PcmWaveAudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        StopProcess();

        lock (_gate)
        {
            _audio = audio;
            _positionFrames = 0;
            _completeAtAudioEnd = false;
            _isPlaying = false;
        }
    }

    public void Play(TimeSpan startTime, bool completeAtAudioEnd)
    {
        ThrowIfDisposed();
        StartPlayback(startTime, completeAtAudioEnd);
    }

    public void Pause()
    {
        StopProcess();
    }

    public void Stop()
    {
        StopProcess();
        Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        bool restart;
        bool completeAtAudioEnd;
        lock (_gate)
        {
            if (_audio == null)
                return;

            _positionFrames = SecondsToFrame(position.TotalSeconds, _audio);
            restart = _isPlaying;
            completeAtAudioEnd = _completeAtAudioEnd;
        }

        if (restart)
            StartPlayback(position, completeAtAudioEnd);
    }

    public void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null)
    {
        lock (_gate)
        {
            _zeroBeatTimeSeconds = zeroBeatTimeSeconds;
            _bpm = bpm > 0 ? bpm : 120;
            _beatsPerMeasure = Math.Max(1, beatsPerMeasure);
            _sectionStartBeats = sectionStarts?
                .Select(v => (int)Math.Round(v))
                .ToHashSet()
                ?? [];
        }
    }

    private void StartPlayback(TimeSpan startTime, bool completeAtAudioEnd)
    {
        StopProcess();

        PcmWaveAudioData audio;
        long startFrame;
        lock (_gate)
        {
            if (_audio == null)
                return;

            audio = _audio;
            startFrame = SecondsToFrame(startTime.TotalSeconds, audio);
            _positionFrames = startFrame;
            _completeAtAudioEnd = completeAtAudioEnd;
            _isPlaying = true;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = _ffplayPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-fflags");
        startInfo.ArgumentList.Add("nobuffer");
        startInfo.ArgumentList.Add("-flags");
        startInfo.ArgumentList.Add("low_delay");
        startInfo.ArgumentList.Add("-probesize");
        startInfo.ArgumentList.Add("32");
        startInfo.ArgumentList.Add("-analyzeduration");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-nodisp");
        startInfo.ArgumentList.Add("-autoexit");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("s16le");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add(audio.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add(audio.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("pipe:0");
        Process? process = null;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("ffplay could not be started.");
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }
        catch (Exception ex)
        {
            if (process != null)
            {
                TryKill(process);
                process.Dispose();
            }

            lock (_gate)
                _isPlaying = false;

            throw new AudioPlaybackUnavailableException("ffplay could not be started.", ex);
        }

        if (process == null)
            return;

        PlaybackProcess playback = new(process, new CancellationTokenSource(), completeAtAudioEnd);
        lock (_gate)
            _playback = playback;

        long sourceFrame = startFrame;
        long framesWritten = 0;
        try
        {
            int prebufferFrames = Math.Min(
                (int)Math.Ceiling(audio.SampleRate * InitialPrebufferSeconds),
                (int)Math.Min(int.MaxValue, audio.FrameCount - startFrame));

            if (prebufferFrames > 0)
            {
                framesWritten = WriteAudioFrames(playback, sourceFrame, prebufferFrames);
                sourceFrame += framesWritten;
                SetPosition(sourceFrame);
            }
        }
        catch (Exception ex)
        {
            StopProcess();
            throw new AudioPlaybackUnavailableException("ffplay could not be primed for playback.", ex);
        }

        _ = PumpAudioAsync(playback, sourceFrame, framesWritten);
    }

    private async Task PumpAudioAsync(PlaybackProcess playback, long startFrame, long framesWritten)
    {
        bool completedNaturally = false;
        Exception? failure = null;

        try
        {
            long sourceFrame = startFrame;
            Stopwatch clock = Stopwatch.StartNew();

            while (!playback.Cancellation.IsCancellationRequested)
            {
                int frames = await WriteAudioFramesAsync(playback, sourceFrame, FramesPerBuffer, playback.Cancellation.Token);
                if (frames <= 0)
                {
                    completedNaturally = true;
                    break;
                }

                sourceFrame += frames;
                framesWritten += frames;
                SetPosition(sourceFrame);

                await DelayUntilAudioCatchesUpAsync(framesWritten, clock, playback.Cancellation.Token);
            }

            TryCloseInput(playback.Process);
            await playback.Process.WaitForExitAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
            TryKill(playback.Process);
        }
        finally
        {
            bool shouldRaiseCompleted;
            lock (_gate)
            {
                if (ReferenceEquals(_playback, playback))
                    _playback = null;

                _isPlaying = false;
                shouldRaiseCompleted = playback.CompleteAtAudioEnd
                    && completedNaturally
                    && failure == null
                    && !playback.StopRequested
                    && TryGetSuccessfulExitCode(playback.Process);
            }

            playback.Cancellation.Dispose();
            playback.Process.Dispose();

            if (failure != null)
                EditorLog.Unexpected(failure, "ffplay audio pump");

            if (shouldRaiseCompleted)
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private int WriteAudioFrames(PlaybackProcess playback, long sourceFrame, int maxFrames)
    {
        int channels = GetChannelCount();
        short[] samples = GetSampleBuffer(playback, maxFrames * channels);
        int frames = FillBuffer(sourceFrame, maxFrames, samples);
        if (frames <= 0)
            return 0;

        int byteCount = frames * channels * sizeof(short);
        byte[] bytes = GetByteBuffer(playback, byteCount);
        Buffer.BlockCopy(samples, 0, bytes, 0, byteCount);
        playback.Process.StandardInput.BaseStream.Write(bytes, 0, byteCount);
        return frames;
    }

    private async Task<int> WriteAudioFramesAsync(PlaybackProcess playback, long sourceFrame, int maxFrames, CancellationToken cancellationToken)
    {
        int channels = GetChannelCount();
        short[] samples = GetSampleBuffer(playback, maxFrames * channels);
        int frames = FillBuffer(sourceFrame, maxFrames, samples);
        if (frames <= 0)
            return 0;

        int byteCount = frames * channels * sizeof(short);
        byte[] bytes = GetByteBuffer(playback, byteCount);
        Buffer.BlockCopy(samples, 0, bytes, 0, byteCount);
        await playback.Process.StandardInput.BaseStream.WriteAsync(bytes.AsMemory(0, byteCount), cancellationToken);
        return frames;
    }

    private static short[] GetSampleBuffer(PlaybackProcess playback, int sampleCount)
    {
        if (playback.SampleBuffer.Length < sampleCount)
            playback.SampleBuffer = new short[sampleCount];

        return playback.SampleBuffer;
    }

    private static byte[] GetByteBuffer(PlaybackProcess playback, int byteCount)
    {
        if (playback.ByteBuffer.Length < byteCount)
            playback.ByteBuffer = new byte[byteCount];

        return playback.ByteBuffer;
    }

    private int FillBuffer(long sourceFrame, int maxFrames, short[] buffer)
    {
        lock (_gate)
        {
            if (_audio == null)
                return 0;

            int channels = _audio.Channels;
            int sampleCount = maxFrames * channels;
            Array.Clear(buffer, 0, sampleCount);

            long availableFrames = Math.Max(0, _audio.FrameCount - sourceFrame);
            int framesToCopy = (int)Math.Min(maxFrames, availableFrames);
            if (framesToCopy <= 0)
                return 0;

            Array.Copy(
                _audio.Samples,
                sourceFrame * channels,
                buffer,
                0,
                framesToCopy * channels);

            if (IsMetronomeEnabled)
                MixMetronome(buffer, sourceFrame, framesToCopy, channels, _audio.SampleRate);

            return framesToCopy;
        }
    }

    private void MixMetronome(short[] buffer, long startFrame, int frameCount, int channels, int sampleRate)
    {
        double beatsPerSecond = _bpm / 60.0;
        if (beatsPerSecond <= 0)
            return;

        for (int frame = 0; frame < frameCount; frame++)
        {
            double timeSeconds = (startFrame + frame) / (double)sampleRate;
            double beatPosition = (timeSeconds - _zeroBeatTimeSeconds) * beatsPerSecond;
            int beat = (int)Math.Floor(beatPosition + 1e-9);
            double beatStartSeconds = _zeroBeatTimeSeconds + (beat / beatsPerSecond);
            double clickTime = timeSeconds - beatStartSeconds;
            if (clickTime is < 0 or >= ClickDurationSeconds)
                continue;

            bool isSection = _sectionStartBeats.Contains(beat);
            bool isMeasure = Mod(beat, _beatsPerMeasure) == 0;
            double frequency = isSection ? 1800 : isMeasure ? 1400 : 1000;
            double gain = isSection ? 0.35 : isMeasure ? 0.28 : 0.20;
            double envelope = 1.0 - (clickTime / ClickDurationSeconds);
            int click = (int)(Math.Sin(2 * Math.PI * frequency * clickTime) * short.MaxValue * gain * envelope);

            int sampleBase = frame * channels;
            for (int channel = 0; channel < channels; channel++)
                buffer[sampleBase + channel] = ClampToInt16(buffer[sampleBase + channel] + click);
        }
    }

    private int GetChannelCount()
    {
        lock (_gate)
            return _audio?.Channels ?? 2;
    }

    private void SetPosition(long sourceFrame)
    {
        lock (_gate)
            _positionFrames = sourceFrame;
    }

    private async Task DelayUntilAudioCatchesUpAsync(long framesWritten, Stopwatch clock, CancellationToken cancellationToken)
    {
        int sampleRate;
        lock (_gate)
            sampleRate = _audio?.SampleRate ?? 48000;

        TimeSpan targetTime = TimeSpan.FromSeconds(framesWritten / (double)sampleRate);
        TimeSpan delay = targetTime - clock.Elapsed;
        if (delay > TimeSpan.FromMilliseconds(1))
            await Task.Delay(delay, cancellationToken);
    }

    private void StopProcess()
    {
        PlaybackProcess? playback;
        lock (_gate)
        {
            playback = _playback;
            _playback = null;
            _isPlaying = false;
            if (playback != null)
                playback.StopRequested = true;
        }

        if (playback == null)
            return;

        playback.Cancellation.Cancel();
        TryKill(playback.Process);
    }

    private static long SecondsToFrame(double seconds, PcmWaveAudioData audio)
    {
        double clampedSeconds = Math.Clamp(seconds, 0, audio.Duration.TotalSeconds);
        return (long)Math.Round(clampedSeconds * audio.SampleRate);
    }

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static short ClampToInt16(int value)
        => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private static void TryCloseInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Close ffplay input");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Terminate ffplay");
        }
    }

    private static bool TryGetSuccessfulExitCode(Process process)
    {
        try
        {
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Read ffplay exit code");
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopProcess();

        lock (_gate)
            _audio = null;

        _disposed = true;
    }
}
