using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

internal sealed partial class PulseAudioPcmPlaybackEngine : IPcmPlaybackEngine, IAudioClockPlaybackEngine
{
    private const int FramesPerBuffer = 512;
    private const double TargetLatencySeconds = 0.055;
    private const double ClickDurationSeconds = 0.035;

    private sealed class PlaybackState(IntPtr stream, CancellationTokenSource cancellation, bool completeAtAudioEnd)
    {
        public IntPtr Stream { get; } = stream;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public bool CompleteAtAudioEnd { get; } = completeAtAudioEnd;
        public bool StopRequested { get; set; }
        public short[] SampleBuffer { get; set; } = [];
        public byte[] ByteBuffer { get; set; } = [];
    }

    private readonly Lock _gate = new();

    private PcmWaveAudioData? _audio;
    private PlaybackState? _playback;
    private long _positionFrames;
    private long _latencyFrames;
    private int _sampleRate = 48000;
    private bool _completeAtAudioEnd;
    private bool _isPlaying;
    private bool _disposed;
    private double _zeroBeatTimeSeconds;
    private double _bpm = 120;
    private int _beatsPerMeasure = 4;
    private HashSet<int> _sectionStartBeats = [];
    private Func<double, double> _beatToSeconds = static beat => beat * 0.5;
    private Func<double, double> _secondsToBeat = static seconds => seconds * 2;

    public event EventHandler? PlaybackCompleted;

    public bool IsLoaded => _audio != null;
    public TimeSpan Duration => _audio?.Duration ?? TimeSpan.Zero;

    public bool IsMetronomeEnabled { get; set; }

    public static bool IsAvailable()
        => OperatingSystem.IsLinux()
        && NativeLibrary.TryLoad("libpulse-simple.so.0", out IntPtr simple)
        && Release(simple)
        && NativeLibrary.TryLoad("libpulse.so.0", out IntPtr pulse)
        && Release(pulse);

    public void Load(PcmWaveAudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        StopPlayback();

        lock (_gate)
        {
            _audio = audio;
            _sampleRate = _audio.SampleRate;
            _positionFrames = 0;
            _latencyFrames = 0;
            _completeAtAudioEnd = false;
            _isPlaying = false;
        }
    }

    public bool TryGetPlaybackTime(out TimeSpan time)
    {
        PlaybackState? playback;
        long submittedFrames;
        int sampleRate;
        lock (_gate)
        {
            playback = _playback;
            submittedFrames = _positionFrames;
            sampleRate = _sampleRate;
        }

        if (playback == null || sampleRate <= 0)
        {
            time = default;
            return false;
        }

        long latencyFrames;
        lock (_gate)
            latencyFrames = _latencyFrames;

        long playedFrames = Math.Max(0, submittedFrames - latencyFrames);
        time = TimeSpan.FromSeconds(playedFrames / (double)sampleRate);
        return true;
    }

    public void Play(TimeSpan startTime, bool completeAtAudioEnd)
    {
        ThrowIfDisposed();
        StopPlayback();

        PcmWaveAudioData audio;
        long startFrame;
        lock (_gate)
        {
            if (_audio == null)
                return;

            audio = _audio;
            startFrame = SecondsToFrame(startTime.TotalSeconds, audio);
            _positionFrames = startFrame;
            _latencyFrames = 0;
            _completeAtAudioEnd = completeAtAudioEnd;
            _isPlaying = true;
        }

        IntPtr stream = OpenStream(audio);
        PlaybackState playback = new(stream, new CancellationTokenSource(), completeAtAudioEnd);
        lock (_gate)
            _playback = playback;

        _ = PumpAudioAsync(playback, startFrame);
    }

    public void Pause()
    {
        StopPlayback();
    }

    public void Stop()
    {
        StopPlayback();
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
            Play(position, completeAtAudioEnd);
    }

    public void UpdateMetronome(
        double zeroBeatTimeSeconds,
        double bpm,
        int beatsPerMeasure,
        IEnumerable<double>? sectionStarts = null,
        Func<double, double>? beatToSeconds = null,
        Func<double, double>? secondsToBeat = null)
    {
        lock (_gate)
        {
            _zeroBeatTimeSeconds = zeroBeatTimeSeconds;
            _bpm = bpm > 0 ? bpm : 120;
            _beatsPerMeasure = Math.Max(1, beatsPerMeasure);
            double secondsPerBeat = 60.0 / _bpm;
            _beatToSeconds = beatToSeconds ?? (beat => _zeroBeatTimeSeconds + (beat * secondsPerBeat));
            _secondsToBeat = secondsToBeat ?? (seconds => (seconds - _zeroBeatTimeSeconds) / secondsPerBeat);
            _sectionStartBeats = sectionStarts?
                .Select(v => (int)Math.Round(v))
                .ToHashSet()
                ?? [];
        }
    }

    private async Task PumpAudioAsync(PlaybackState playback, long startFrame)
    {
        bool completedNaturally = false;
        Exception? failure = null;

        try
        {
            long sourceFrame = startFrame;
            while (!playback.Cancellation.IsCancellationRequested)
            {
                int frames = WriteAudioFrames(playback.Stream, sourceFrame, FramesPerBuffer, playback.CompleteAtAudioEnd);
                if (frames <= 0)
                {
                    completedNaturally = true;
                    break;
                }

                sourceFrame += frames;
                SetPosition(sourceFrame);
                UpdateLatency(playback.Stream);
                await Task.Yield();
            }

            if (completedNaturally)
                PulseNative.Drain(playback.Stream);
        }
        catch (Exception ex)
        {
            failure = ex;
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
                    && !playback.StopRequested;
            }

            playback.Cancellation.Dispose();
            PulseNative.Free(playback.Stream);

            if (failure != null && !playback.StopRequested)
                EditorLog.Unexpected(failure, "PulseAudio playback");

            if (shouldRaiseCompleted)
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private int WriteAudioFrames(IntPtr stream, long sourceFrame, int maxFrames, bool completeAtAudioEnd)
    {
        PlaybackState? playback;
        int channels = GetChannelCount();
        lock (_gate)
            playback = _playback;

        if (playback == null || playback.Stream != stream)
            return 0;

        short[] samples = GetSampleBuffer(playback, maxFrames * channels);
        int frames = FillBuffer(sourceFrame, maxFrames, completeAtAudioEnd, samples);
        if (frames <= 0)
            return 0;

        int byteCount = frames * channels * sizeof(short);
        byte[] bytes = GetByteBuffer(playback, byteCount);
        Buffer.BlockCopy(samples, 0, bytes, 0, byteCount);
        PulseNative.Write(stream, bytes, byteCount);
        return frames;
    }

    private static short[] GetSampleBuffer(PlaybackState playback, int sampleCount)
    {
        if (playback.SampleBuffer.Length < sampleCount)
            playback.SampleBuffer = new short[sampleCount];

        return playback.SampleBuffer;
    }

    private static byte[] GetByteBuffer(PlaybackState playback, int byteCount)
    {
        if (playback.ByteBuffer.Length < byteCount)
            playback.ByteBuffer = new byte[byteCount];

        return playback.ByteBuffer;
    }

    private int FillBuffer(long sourceFrame, int maxFrames, bool completeAtAudioEnd, short[] buffer)
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
            if (framesToCopy <= 0 && completeAtAudioEnd)
                return 0;

            if (framesToCopy > 0)
            {
                Array.Copy(
                    _audio.Samples,
                    sourceFrame * channels,
                    buffer,
                    0,
                    framesToCopy * channels);
            }

            int framesToWrite = completeAtAudioEnd ? framesToCopy : maxFrames;
            if (IsMetronomeEnabled)
                MixMetronome(buffer, sourceFrame, framesToWrite, channels, _audio.SampleRate);

            return framesToWrite;
        }
    }

    private void MixMetronome(short[] buffer, long startFrame, int frameCount, int channels, int sampleRate)
    {
        for (int frame = 0; frame < frameCount; frame++)
        {
            double timeSeconds = (startFrame + frame) / (double)sampleRate;
            double beatPosition = _secondsToBeat(timeSeconds);
            int beat = (int)Math.Floor(beatPosition + 1e-9);
            double beatStartSeconds = _beatToSeconds(beat);
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

    private static IntPtr OpenStream(PcmWaveAudioData audio)
    {
        PulseNative.SampleSpec sampleSpec = new()
        {
            Format = PulseNative.SampleS16Le,
            Rate = (uint)audio.SampleRate,
            Channels = (byte)audio.Channels
        };

        int bytesPerFrame = audio.Channels * sizeof(short);
        uint targetLength = (uint)Math.Max(bytesPerFrame * FramesPerBuffer, audio.SampleRate * bytesPerFrame * TargetLatencySeconds);
        PulseNative.BufferAttr bufferAttr = new()
        {
            MaxLength = targetLength * 2,
            TargetLength = targetLength,
            PreBuffer = 0,
            MinRequest = (uint)(FramesPerBuffer * bytesPerFrame),
            FragmentSize = uint.MaxValue
        };

        return PulseNative.New("Just Dance Editor", "Timeline preview", sampleSpec, bufferAttr);
    }

    private void StopPlayback()
    {
        PlaybackState? playback;
        lock (_gate)
        {
            playback = _playback;
            _playback = null;
            _isPlaying = false;
            playback?.StopRequested = true;
        }

        if (playback == null)
            return;

        playback.Cancellation.Cancel();
        PulseNative.TryFlush(playback.Stream);
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

    private void UpdateLatency(IntPtr stream)
    {
        try
        {
            TimeSpan latency = PulseNative.GetLatency(stream);
            lock (_gate)
                _latencyFrames = (long)Math.Round(latency.TotalSeconds * _sampleRate);
        }
        catch (Exception ex)
        {
            EditorLog.Fallback(ex, "Read PulseAudio latency");
        }
    }

    private static bool Release(IntPtr library)
    {
        NativeLibrary.Free(library);
        return true;
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopPlayback();

        lock (_gate)
            _audio = null;

        _disposed = true;
    }

    private static partial class PulseNative
    {
        private const string PulseLibrary = "libpulse.so.0";
        private const string PulseSimpleLibrary = "libpulse-simple.so.0";

        public const int SampleS16Le = 3;
        private const int StreamPlayback = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct SampleSpec
        {
            public int Format;
            public uint Rate;
            public byte Channels;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BufferAttr
        {
            public uint MaxLength;
            public uint TargetLength;
            public uint PreBuffer;
            public uint MinRequest;
            public uint FragmentSize;
        }

        public static IntPtr New(string applicationName, string streamName, SampleSpec sampleSpec, BufferAttr bufferAttr)
        {
            IntPtr stream = PaSimpleNew(
                null,
                applicationName,
                StreamPlayback,
                null,
                streamName,
                ref sampleSpec,
                IntPtr.Zero,
                ref bufferAttr,
                out int error);

            if (stream == IntPtr.Zero)
                throw new AudioPlaybackUnavailableException($"PulseAudio stream could not be opened: {GetError(error)}");

            return stream;
        }

        public static unsafe void Write(IntPtr stream, byte[] bytes, int byteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(byteCount, bytes.Length);

            fixed (byte* data = bytes)
            {
                int result = PaSimpleWrite(stream, data, (nuint)byteCount, out int error);
                if (result < 0)
                    throw new AudioPlaybackUnavailableException($"PulseAudio write failed: {GetError(error)}");
            }
        }

        public static void Drain(IntPtr stream)
        {
            int result = PaSimpleDrain(stream, out int error);
            if (result < 0)
                throw new AudioPlaybackUnavailableException($"PulseAudio drain failed: {GetError(error)}");
        }

        public static TimeSpan GetLatency(IntPtr stream)
        {
            ulong latencyUsec = PaSimpleGetLatency(stream, out int error);
            if (latencyUsec == ulong.MaxValue)
                throw new AudioPlaybackUnavailableException($"PulseAudio latency query failed: {GetError(error)}");

            return TimeSpan.FromTicks((long)latencyUsec * 10);
        }

        public static void TryFlush(IntPtr stream)
        {
            try
            {
                int result = PaSimpleFlush(stream, out int error);
                if (result < 0)
                    throw new AudioPlaybackUnavailableException($"PulseAudio flush failed: {GetError(error)}");
            }
            catch (Exception ex)
            {
                EditorLog.Fallback(ex, "Flush PulseAudio stream");
            }
        }

        public static void Free(IntPtr stream)
        {
            PaSimpleFree(stream);
        }

        private static string GetError(int error)
        {
            IntPtr text = PaStrError(error);
            return Marshal.PtrToStringUTF8(text) ?? $"error {error}";
        }

        [LibraryImport(PulseSimpleLibrary, EntryPoint = "pa_simple_new", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial IntPtr PaSimpleNew(
            string? server,
            string name,
            int direction,
            string? device,
            string streamName,
            ref SampleSpec sampleSpec,
            IntPtr channelMap,
            ref BufferAttr bufferAttr,
            out int error);

        [LibraryImport(PulseSimpleLibrary, EntryPoint = "pa_simple_write")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe partial int PaSimpleWrite(IntPtr stream, byte* data, nuint bytes, out int error);

        [LibraryImport(PulseSimpleLibrary, EntryPoint = "pa_simple_drain")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int PaSimpleDrain(IntPtr stream, out int error);

        [LibraryImport(PulseSimpleLibrary, EntryPoint = "pa_simple_flush")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int PaSimpleFlush(IntPtr stream, out int error);

        [LibraryImport(PulseSimpleLibrary, EntryPoint = "pa_simple_get_latency")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial ulong PaSimpleGetLatency(IntPtr stream, out int error);

        [LibraryImport(PulseSimpleLibrary, EntryPoint = "pa_simple_free")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void PaSimpleFree(IntPtr stream);

        [LibraryImport(PulseLibrary, EntryPoint = "pa_strerror")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial IntPtr PaStrError(int error);
    }
}
