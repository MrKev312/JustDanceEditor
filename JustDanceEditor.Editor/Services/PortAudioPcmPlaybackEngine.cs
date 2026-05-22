using PortAudioSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using PortAudioStream = PortAudioSharp.Stream;

namespace JustDanceEditor.Editor.Services;

internal sealed class PortAudioPcmPlaybackEngine : IDisposable
{
    private const uint FramesPerBuffer = 1024;
    private const double ClickDurationSeconds = 0.035;

    private readonly object _gate = new();
    private readonly PortAudioStream.Callback _callback;
    private readonly PortAudioStream.FinishedCallback _finishedCallback;

    private PcmWaveAudioData? _audio;
    private PortAudioStream? _stream;
    private short[] _callbackBuffer = [];
    private long _positionFrames;
    private bool _completeAtAudioEnd;
    private bool _completedNaturally;
    private bool _streamCompleted;
    private bool _isPlaying;
    private bool _disposed;

    private bool _metronomeEnabled;
    private double _zeroBeatTimeSeconds;
    private double _bpm = 120;
    private int _beatsPerMeasure = 4;
    private HashSet<int> _sectionStartBeats = [];

    public PortAudioPcmPlaybackEngine()
    {
        _callback = OnAudioRequested;
        _finishedCallback = OnStreamFinished;
    }

    public event EventHandler? PlaybackCompleted;

    public bool IsLoaded => _audio != null;
    public TimeSpan Duration => _audio?.Duration ?? TimeSpan.Zero;

    public bool IsMetronomeEnabled
    {
        get => _metronomeEnabled;
        set => _metronomeEnabled = value;
    }

    public void Load(string wavPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("The audio preview file does not exist.", wavPath);

        CloseStream();

        lock (_gate)
        {
            _audio = PcmWaveAudioData.Read(wavPath);
            _positionFrames = 0;
            _completedNaturally = false;
            _streamCompleted = false;
        }
    }

    public void Play(TimeSpan startTime, bool completeAtAudioEnd)
    {
        ThrowIfDisposed();

        bool closeCompletedStream;
        lock (_gate)
        {
            if (_audio == null)
                return;

            closeCompletedStream = _streamCompleted;
        }

        if (closeCompletedStream)
            CloseStream();

        lock (_gate)
        {
            if (_audio == null)
                return;

            _positionFrames = SecondsToFrame(startTime.TotalSeconds, _audio);
            _completeAtAudioEnd = completeAtAudioEnd;
            _completedNaturally = false;
            _isPlaying = true;
        }

        EnsureStream();
        if (_stream is { IsStopped: true })
            _stream.Start();
    }

    public void Pause()
    {
        PortAudioStream? stream;
        lock (_gate)
        {
            _isPlaying = false;
            _completedNaturally = false;
            stream = _stream;
        }

        if (stream is { IsStopped: false })
            stream.Stop();
    }

    public void Stop()
    {
        Pause();
        Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_audio == null)
                return;

            _positionFrames = SecondsToFrame(position.TotalSeconds, _audio);
            _completedNaturally = false;
        }
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

    private void EnsureStream()
    {
        lock (_gate)
        {
            if (_stream != null || _audio == null)
                return;

            bool runtimeAdded = false;
            try
            {
                PortAudioRuntime.AddReference();
                runtimeAdded = true;

                int device = PortAudio.DefaultOutputDevice;
                if (device == PortAudio.NoDevice)
                    throw new InvalidOperationException("No default audio output device is available.");

                DeviceInfo deviceInfo = PortAudio.GetDeviceInfo(device);
                StreamParameters output = new()
                {
                    device = device,
                    channelCount = _audio.Channels,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = deviceInfo.defaultLowOutputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero
                };

                _stream = new PortAudioStream(
                    null,
                    output,
                    _audio.SampleRate,
                    FramesPerBuffer,
                    StreamFlags.ClipOff,
                    _callback,
                    this);
                _stream.SetFinishedCallback(_finishedCallback);
                _streamCompleted = false;
            }
            catch
            {
                if (runtimeAdded)
                    PortAudioRuntime.ReleaseReference();

                throw;
            }
        }
    }

    private StreamCallbackResult OnAudioRequested(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        bool shouldComplete = false;

        lock (_gate)
        {
            if (_audio == null || !_isPlaying)
            {
                WriteSilence(output, frameCount, _audio?.Channels ?? 2);
                return StreamCallbackResult.Continue;
            }

            int channels = _audio.Channels;
            int sampleCount = checked((int)frameCount * channels);
            if (_callbackBuffer.Length < sampleCount)
                _callbackBuffer = new short[sampleCount];

            Array.Clear(_callbackBuffer, 0, sampleCount);

            long sourceFrame = _positionFrames;
            long availableFrames = Math.Max(0, _audio.FrameCount - sourceFrame);
            int framesToCopy = (int)Math.Min(frameCount, availableFrames);
            if (framesToCopy > 0)
            {
                Array.Copy(
                    _audio.Samples,
                    sourceFrame * channels,
                    _callbackBuffer,
                    0,
                    framesToCopy * channels);
            }

            if (_metronomeEnabled)
                MixMetronome(_callbackBuffer, sourceFrame, (int)frameCount, channels, _audio.SampleRate);

            _positionFrames += frameCount;
            if (_completeAtAudioEnd && _positionFrames >= _audio.FrameCount)
            {
                _positionFrames = _audio.FrameCount;
                _isPlaying = false;
                _completedNaturally = true;
                _streamCompleted = true;
                shouldComplete = true;
            }

            Marshal.Copy(_callbackBuffer, 0, output, sampleCount);
        }

        return shouldComplete ? StreamCallbackResult.Complete : StreamCallbackResult.Continue;
    }

    private void OnStreamFinished(IntPtr userData)
    {
        bool completedNaturally;
        lock (_gate)
        {
            _streamCompleted = true;
            completedNaturally = _completedNaturally;
            _completedNaturally = false;
        }

        if (completedNaturally)
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
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
            double beatStartSeconds = _zeroBeatTimeSeconds + beat / beatsPerSecond;
            double clickTime = timeSeconds - beatStartSeconds;
            if (clickTime < 0 || clickTime >= ClickDurationSeconds)
                continue;

            bool isSection = _sectionStartBeats.Contains(beat);
            bool isMeasure = Mod(beat, _beatsPerMeasure) == 0;
            double frequency = isSection ? 1800 : isMeasure ? 1400 : 1000;
            double gain = isSection ? 0.35 : isMeasure ? 0.28 : 0.20;
            double envelope = 1.0 - clickTime / ClickDurationSeconds;
            int click = (int)(Math.Sin(2 * Math.PI * frequency * clickTime) * short.MaxValue * gain * envelope);

            int sampleBase = frame * channels;
            for (int channel = 0; channel < channels; channel++)
                buffer[sampleBase + channel] = ClampToInt16(buffer[sampleBase + channel] + click);
        }
    }

    private static void WriteSilence(IntPtr output, uint frameCount, int channels)
    {
        int bytes = checked((int)frameCount * Math.Max(1, channels) * sizeof(short));
        byte[] silence = new byte[bytes];
        Marshal.Copy(silence, 0, output, silence.Length);
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

    private void CloseStream()
    {
        PortAudioStream? stream;
        lock (_gate)
        {
            stream = _stream;
            _stream = null;
            _streamCompleted = false;
        }

        if (stream == null)
            return;

        try
        {
            if (!stream.IsStopped)
                stream.Stop();
        }
        catch
        {
        }

        stream.Dispose();
        PortAudioRuntime.ReleaseReference();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            _isPlaying = false;
            _completedNaturally = false;
        }

        CloseStream();

        lock (_gate)
            _audio = null;

        _disposed = true;
    }
}

internal static class PortAudioRuntime
{
    private static readonly object Gate = new();
    private static int _referenceCount;

    public static void AddReference()
    {
        lock (Gate)
        {
            if (_referenceCount == 0)
            {
                PortAudio.LoadNativeLibrary();
                PortAudio.Initialize();
            }

            _referenceCount++;
        }
    }

    public static void ReleaseReference()
    {
        lock (Gate)
        {
            if (_referenceCount <= 0)
                return;

            _referenceCount--;
            if (_referenceCount == 0)
                PortAudio.Terminate();
        }
    }
}
