using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

internal sealed class SharedVideoPreviewLease : IDisposable
{
    private SharedVideoPreviewSession? _session;

    public SharedVideoPreviewLease(SharedVideoPreviewSession session)
    {
        _session = session;
    }

    public SharedVideoPreviewSession Session
        => _session ?? throw new ObjectDisposedException(nameof(SharedVideoPreviewLease));

    public void Dispose()
    {
        SharedVideoPreviewSession? session = Interlocked.Exchange(ref _session, null);
        if (session == null)
            return;

        SharedVideoPreviewSessions.Release(session);
    }
}

internal static class SharedVideoPreviewSessions
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, SharedVideoPreviewSession> Sessions = new(PathComparer);

    public static SharedVideoPreviewLease Acquire(string videoPath)
    {
        string normalizedPath = Path.GetFullPath(videoPath);

        lock (Sync)
        {
            if (!Sessions.TryGetValue(normalizedPath, out SharedVideoPreviewSession? session))
            {
                session = new SharedVideoPreviewSession(normalizedPath);
                Sessions.Add(normalizedPath, session);
            }

            session.AddReference();
            return new SharedVideoPreviewLease(session);
        }
    }

    public static void Release(SharedVideoPreviewSession session)
    {
        lock (Sync)
        {
            if (!session.ReleaseReference())
                return;

            Sessions.Remove(session.VideoPath);
            session.Dispose();
        }
    }
}

internal sealed class SharedVideoPreviewSession : IDisposable
{
    private const double RestartStreamDriftSeconds = 0.45;
    private const double StillFrameToleranceSeconds = 0.04;
    private const double StillFrameRestartDistanceSeconds = 2.0;
    private const double MaxStillFrameDisplayRate = 144.0;

    private readonly FfmpegVideoFrameReader _frameReader = new();
    private readonly Stopwatch _streamClock = new();
    private readonly object _stateLock = new();
    private readonly object _referenceLock = new();

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _stillFrameCts;
    private Task? _streamTask;
    private Task? _stillFrameTask;
    private FfmpegVideoFrameInfo? _videoInfo;
    private string? _streamVideoPath;
    private double _streamVideoStartSeconds;
    private double _lastStillFrameSeconds = double.NaN;
    private double _requestedStillFrameSeconds = double.NaN;
    private bool _lastRequestWasPlaying;
    private int _currentFrameWidth;
    private int _currentFrameHeight;
    private int _referenceCount;
    private bool _disposed;

    public SharedVideoPreviewSession(string videoPath)
    {
        VideoPath = videoPath;
        StatusText = "Loading video...";
        StartLoad();
    }

    public event EventHandler? StateChanged;

    public string VideoPath { get; }

    public WriteableBitmap? CurrentFrame { get; private set; }

    public int FrameVersion { get; private set; }

    public string StatusText { get; private set; }

    public void AddReference()
    {
        lock (_referenceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _referenceCount++;
        }
    }

    public bool ReleaseReference()
    {
        lock (_referenceLock)
        {
            if (_referenceCount <= 0)
                return true;

            _referenceCount--;
            return _referenceCount == 0;
        }
    }

    public void RequestFrame(double targetSeconds, bool isPlaying)
    {
        if (_disposed)
            return;

        targetSeconds = Math.Max(0, targetSeconds);
        lock (_stateLock)
        {
            _requestedStillFrameSeconds = targetSeconds;
            _lastRequestWasPlaying = isPlaying;
        }

        FfmpegVideoFrameInfo? info = _videoInfo;
        if (info == null)
            return;

        if (isPlaying)
        {
            EnsureStream(targetSeconds, info);
            return;
        }

        StopStream();
        RenderStillFrame(info);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        CancelStillFrame();
        StopStream();
        DisposeCurrentFrameOnUiThread();
    }

    private void StartLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadVideoAsync(_loadCts.Token);
    }

    private async Task LoadVideoAsync(CancellationToken cancellationToken)
    {
        try
        {
            FfmpegVideoFrameInfo info = await _frameReader.GetVideoInfoAsync(VideoPath, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                    return;

                _videoInfo = info;
                StatusText = "Ready";
                RaiseStateChanged();

                double targetSeconds;
                bool isPlaying;
                lock (_stateLock)
                {
                    targetSeconds = _requestedStillFrameSeconds;
                    isPlaying = _lastRequestWasPlaying;
                }

                if (!double.IsNaN(targetSeconds))
                    RequestFrame(targetSeconds, isPlaying);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                    return;

                StatusText = $"Video unavailable: {ex.Message}";
                RaiseStateChanged();
            });
        }
    }

    private void EnsureStream(double targetSeconds, FfmpegVideoFrameInfo info)
    {
        if (_streamTask is { IsCompleted: false } &&
            string.Equals(_streamVideoPath, VideoPath, StringComparison.Ordinal) &&
            Math.Abs(GetEstimatedStreamVideoSeconds() - targetSeconds) <= RestartStreamDriftSeconds)
        {
            return;
        }

        CancelStillFrame();
        StopStream();

        _streamCts = new CancellationTokenSource();
        CancellationToken token = _streamCts.Token;
        _streamVideoPath = VideoPath;
        _streamVideoStartSeconds = targetSeconds;
        _streamClock.Restart();
        StatusText = string.Empty;
        RaiseStateChanged();

        _streamTask = Task.Run(async () =>
        {
            try
            {
                await _frameReader.StreamFramesAsync(
                    VideoPath,
                    targetSeconds,
                    info,
                    async (frameBytes, frameInfo, _, frameToken) =>
                    {
                        await ApplyFrameBytesOnUiThreadAsync(frameBytes, frameInfo, frameToken);
                        return true;
                    },
                    paceFrames: true,
                    token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested && !_disposed)
                    {
                        StatusText = $"Video preview stopped: {ex.Message}";
                        RaiseStateChanged();
                    }
                });
            }
        }, token);
    }

    private void RenderStillFrame(FfmpegVideoFrameInfo info)
    {
        ClearCompletedStillFrameTask();

        double targetSeconds = GetRequestedStillFrameSeconds();
        if (CurrentFrame != null && !double.IsNaN(_lastStillFrameSeconds) && Math.Abs(_lastStillFrameSeconds - targetSeconds) <= StillFrameToleranceSeconds)
            return;

        if (_stillFrameTask is { IsCompleted: false })
            return;

        _stillFrameCts = new CancellationTokenSource();
        CancellationToken token = _stillFrameCts.Token;

        _stillFrameTask = Task.Run(
            () => RenderStillFrameLoopAsync(info, token),
            token);
    }

    private async Task RenderStillFrameLoopAsync(FfmpegVideoFrameInfo info, CancellationToken token)
    {
        try
        {
            double frameDurationSeconds = 1d / Math.Max(1, info.FrameRate);
            double applyToleranceSeconds = Math.Max(StillFrameToleranceSeconds, frameDurationSeconds * 0.5);
            double displayStepSeconds = Math.Max(StillFrameToleranceSeconds * 0.5, frameDurationSeconds * 0.75);
            double minDisplayTicks = Stopwatch.Frequency / MaxStillFrameDisplayRate;
            long lastDisplayTimestamp = 0;

            while (!token.IsCancellationRequested)
            {
                double streamStartSeconds = GetRequestedStillFrameSeconds();
                if (double.IsNaN(streamStartSeconds))
                    return;

                if (!double.IsNaN(_lastStillFrameSeconds) && Math.Abs(_lastStillFrameSeconds - streamStartSeconds) <= StillFrameToleranceSeconds)
                    return;

                bool restartStream = false;
                bool displayedFrameInStream = false;
                await _frameReader.StreamFramesAsync(
                    VideoPath,
                    streamStartSeconds,
                    info,
                    async (frameBytes, frameInfo, frameSeconds, frameToken) =>
                    {
                        double requestedSeconds = GetRequestedStillFrameSeconds();
                        if (double.IsNaN(requestedSeconds))
                            return false;

                        bool requestMovedBeyondStream = ShouldRestartStillFrameStream(requestedSeconds, frameSeconds, applyToleranceSeconds);
                        bool forceDisplay = !displayedFrameInStream || Math.Abs(frameSeconds - requestedSeconds) <= applyToleranceSeconds;
                        bool canDisplayNow = forceDisplay || lastDisplayTimestamp == 0 || Stopwatch.GetTimestamp() - lastDisplayTimestamp >= minDisplayTicks;
                        if (canDisplayNow && (forceDisplay || (!requestMovedBeyondStream && ShouldDisplayStillFrame(frameSeconds, displayStepSeconds))))
                        {
                            displayedFrameInStream = true;
                            lastDisplayTimestamp = Stopwatch.GetTimestamp();
                            _lastStillFrameSeconds = frameSeconds;
                            await ApplyFrameBytesOnUiThreadAsync(frameBytes, frameInfo, frameToken);

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (!frameToken.IsCancellationRequested && !_disposed)
                                {
                                    StatusText = string.Empty;
                                    RaiseStateChanged();
                                }
                            });
                        }

                        if (requestMovedBeyondStream)
                        {
                            restartStream = true;
                            return false;
                        }

                        double latestRequestedSeconds = GetRequestedStillFrameSeconds();
                        if (double.IsNaN(latestRequestedSeconds))
                            return false;

                        if (ShouldRestartStillFrameStream(latestRequestedSeconds, frameSeconds, applyToleranceSeconds))
                        {
                            restartStream = true;
                            return false;
                        }

                        return Math.Abs(latestRequestedSeconds - frameSeconds) > applyToleranceSeconds;
                    },
                    paceFrames: false,
                    token);

                if (!restartStream)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && !_disposed)
                {
                    StatusText = $"Video unavailable: {ex.Message}";
                    RaiseStateChanged();
                }
            });
        }
    }

    private bool ShouldDisplayStillFrame(double frameSeconds, double displayStepSeconds)
        => double.IsNaN(_lastStillFrameSeconds)
            || Math.Abs(frameSeconds - _lastStillFrameSeconds) >= displayStepSeconds;

    private static bool ShouldRestartStillFrameStream(double requestedSeconds, double frameSeconds, double applyToleranceSeconds)
        => requestedSeconds < frameSeconds - applyToleranceSeconds
            || requestedSeconds > frameSeconds + StillFrameRestartDistanceSeconds;

    private double GetEstimatedStreamVideoSeconds()
    {
        if (!_streamClock.IsRunning)
            return _streamVideoStartSeconds;

        return _streamVideoStartSeconds + _streamClock.Elapsed.TotalSeconds;
    }

    private async Task ApplyFrameBytesOnUiThreadAsync(byte[] frameBytes, FfmpegVideoFrameInfo info, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested || _disposed)
                return;

            EnsureCurrentFrame(info);
            WriteableBitmap? bitmap = CurrentFrame;
            if (bitmap == null)
                return;

            CopyPixelsToBitmap(frameBytes, bitmap, info.OutputWidth, info.OutputHeight);
            FrameVersion++;
            RaiseStateChanged();
        }, DispatcherPriority.Render);
    }

    private void EnsureCurrentFrame(FfmpegVideoFrameInfo info)
    {
        if (CurrentFrame != null && _currentFrameWidth == info.OutputWidth && _currentFrameHeight == info.OutputHeight)
            return;

        CurrentFrame?.Dispose();
        CurrentFrame = new WriteableBitmap(
            new PixelSize(info.OutputWidth, info.OutputHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        _currentFrameWidth = info.OutputWidth;
        _currentFrameHeight = info.OutputHeight;
    }

    private static void CopyPixelsToBitmap(byte[] bgraPixels, WriteableBitmap bitmap, int width, int height)
    {
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        int sourceStride = width * 4;
        int targetStride = framebuffer.RowBytes;

        if (targetStride == sourceStride)
        {
            Marshal.Copy(bgraPixels, 0, framebuffer.Address, sourceStride * height);
            return;
        }

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(bgraPixels, y * sourceStride, IntPtr.Add(framebuffer.Address, y * targetStride), sourceStride);
        }
    }

    private void CancelStillFrame(bool clearRequest = true)
    {
        if (clearRequest)
        {
            lock (_stateLock)
            {
                _requestedStillFrameSeconds = double.NaN;
            }
        }

        CancellationTokenSource? stillFrameCts = _stillFrameCts;
        Task? stillFrameTask = _stillFrameTask;
        try
        {
            stillFrameCts?.Cancel();
        }
        catch
        {
        }

        _stillFrameCts = null;
        _stillFrameTask = null;

        if (stillFrameCts == null)
            return;

        if (stillFrameTask is { IsCompleted: false })
            _ = stillFrameTask.ContinueWith(_ => stillFrameCts.Dispose(), TaskScheduler.Default);
        else
            stillFrameCts.Dispose();
    }

    private double GetRequestedStillFrameSeconds()
    {
        lock (_stateLock)
        {
            return _requestedStillFrameSeconds;
        }
    }

    private void ClearCompletedStillFrameTask()
    {
        if (_stillFrameTask is not { IsCompleted: true })
            return;

        _stillFrameCts?.Dispose();
        _stillFrameCts = null;
        _stillFrameTask = null;
    }

    private void StopStream()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
        _streamTask = null;
        _streamVideoPath = null;
        _streamClock.Reset();
    }

    private void DisposeCurrentFrameOnUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(DisposeCurrentFrameOnUiThread, DispatcherPriority.Render);
            return;
        }

        CurrentFrame?.Dispose();
        CurrentFrame = null;
        _currentFrameWidth = 0;
        _currentFrameHeight = 0;
        FrameVersion++;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RaiseStateChanged, DispatcherPriority.Render);
            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
