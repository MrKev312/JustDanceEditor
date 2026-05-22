using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Video Preview", "View/Preview")]
public partial class VideoToolViewModel : TimelineToolViewModel, IDisposable
{
    private const double RestartStreamDriftSeconds = 0.45;
    private const double StillFrameToleranceSeconds = 0.04;
    private static readonly TimeSpan StillFrameDebounce = TimeSpan.FromMilliseconds(25);

    private readonly FfmpegVideoFrameReader _frameReader = new();
    private readonly Stopwatch _streamClock = new();
    private readonly object _stillFrameLock = new();

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _stillFrameCts;
    private Task? _streamTask;
    private Task? _stillFrameTask;
    private FfmpegVideoFrameInfo? _videoInfo;
    private string? _loadedVideoPath;
    private string? _streamVideoPath;
    private double _streamVideoStartSeconds;
    private double _lastStillFrameSeconds = double.NaN;
    private double _requestedStillFrameSeconds = double.NaN;
    private byte[] _stillFrameBytes = [];
    private int _currentFrameWidth;
    private int _currentFrameHeight;
    private bool _disposed;

    [ObservableProperty]
    public partial WriteableBitmap? CurrentFrame { get; set; }

    [ObservableProperty]
    public partial int CurrentFrameVersion { get; set; }

    [ObservableProperty]
    public partial string VideoStatusText { get; set; } = "No active timeline";

    partial void OnCurrentFrameChanging(WriteableBitmap? oldValue, WriteableBitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        SyncMedia();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        StopStream();
        CancelStillFrame();
        _loadCts?.Cancel();
        _loadedVideoPath = null;
        _videoInfo = null;
        _lastStillFrameSeconds = double.NaN;
        ClearFrameOnUiThread();
        VideoStatusText = "No active timeline";
    }

    protected override void OnTimelinePropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(TimelineEditorViewModel.VideoPath))
            SyncMedia();

        if (propertyName == nameof(TimelineEditorViewModel.VideoOffset))
            SyncTime();
    }

    protected override void OnTimeChanged()
    {
        SyncTime();
    }

    private void SyncMedia()
    {
        string? videoPath = ActiveTimeline?.VideoPath;
        if (string.Equals(videoPath, _loadedVideoPath, StringComparison.Ordinal))
            return;

        StopStream();
        CancelStillFrame();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _loadedVideoPath = videoPath;
        _videoInfo = null;
        _lastStillFrameSeconds = double.NaN;
        ClearFrameOnUiThread();

        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            VideoStatusText = ActiveTimeline == null ? "No active timeline" : "No video found";
            return;
        }

        VideoStatusText = "Loading video...";
        _ = LoadVideoAsync(videoPath, _loadCts.Token);
    }

    private async Task LoadVideoAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            FfmpegVideoFrameInfo info = await _frameReader.GetVideoInfoAsync(videoPath, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                _videoInfo = info;
                VideoStatusText = "Ready";
                SyncTime();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    VideoStatusText = $"Video unavailable: {ex.Message}";
            });
        }
    }

    private void SyncTime()
    {
        if (_disposed || ActiveTimeline == null || _videoInfo == null || string.IsNullOrWhiteSpace(_loadedVideoPath))
            return;

        double targetSeconds = GetTargetVideoSeconds(ActiveTimeline);
        if (ActiveTimeline.Playback.IsPlaying)
        {
            EnsureStream(targetSeconds);
            return;
        }

        StopStream();
        RenderStillFrame(targetSeconds);
    }

    private void EnsureStream(double targetSeconds)
    {
        if (_videoInfo == null || string.IsNullOrWhiteSpace(_loadedVideoPath))
            return;

        if (_streamTask is { IsCompleted: false } &&
            string.Equals(_streamVideoPath, _loadedVideoPath, StringComparison.Ordinal) &&
            Math.Abs(GetEstimatedStreamVideoSeconds() - targetSeconds) <= RestartStreamDriftSeconds)
        {
            return;
        }

        CancelStillFrame();
        StopStream();

        _streamCts = new CancellationTokenSource();
        CancellationToken token = _streamCts.Token;
        string videoPath = _loadedVideoPath;
        FfmpegVideoFrameInfo info = _videoInfo;
        _streamVideoPath = videoPath;
        _streamVideoStartSeconds = targetSeconds;
        _streamClock.Restart();
        VideoStatusText = string.Empty;

        _streamTask = Task.Run(async () =>
        {
            try
            {
                await _frameReader.StreamFramesAsync(
                    videoPath,
                    targetSeconds,
                    info,
                    (frameBytes, frameInfo, frameToken) => ApplyFrameBytesOnUiThreadAsync(frameBytes, frameInfo, frameToken),
                    token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        VideoStatusText = $"Video preview stopped: {ex.Message}";
                });
            }
        }, token);
    }

    private void RenderStillFrame(double targetSeconds)
    {
        if (_videoInfo == null || string.IsNullOrWhiteSpace(_loadedVideoPath))
            return;

        if (CurrentFrame != null && !double.IsNaN(_lastStillFrameSeconds) && Math.Abs(_lastStillFrameSeconds - targetSeconds) <= StillFrameToleranceSeconds)
            return;

        lock (_stillFrameLock)
        {
            _requestedStillFrameSeconds = targetSeconds;
        }

        if (_stillFrameTask is { IsCompleted: false })
            return;

        _stillFrameCts?.Dispose();
        _stillFrameCts = new CancellationTokenSource();
        CancellationToken token = _stillFrameCts.Token;
        string videoPath = _loadedVideoPath;
        FfmpegVideoFrameInfo info = _videoInfo;

        _stillFrameTask = Task.Run(
            () => RunStillFrameLoopAsync(videoPath, info, token),
            token);
    }

    private async Task RunStillFrameLoopAsync(string videoPath, FfmpegVideoFrameInfo info, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(StillFrameDebounce, token);
                double targetSeconds = GetRequestedStillFrameSeconds();
                if (double.IsNaN(targetSeconds))
                    return;

                byte[] frameBytes = GetStillFrameBuffer(info);
                await _frameReader.ReadFramePixelsAsync(videoPath, targetSeconds, info, frameBytes, token);
                double latestRequestedSeconds = GetRequestedStillFrameSeconds();
                if (Math.Abs(latestRequestedSeconds - targetSeconds) > StillFrameToleranceSeconds)
                {
                    continue;
                }

                _lastStillFrameSeconds = targetSeconds;
                await ApplyFrameBytesOnUiThreadAsync(frameBytes, info, token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        VideoStatusText = string.Empty;
                });

                latestRequestedSeconds = GetRequestedStillFrameSeconds();
                if (Math.Abs(latestRequestedSeconds - targetSeconds) <= StillFrameToleranceSeconds)
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
                if (!token.IsCancellationRequested)
                    VideoStatusText = $"Video unavailable: {ex.Message}";
            });
        }
    }

    private static double GetTargetVideoSeconds(TimelineEditorViewModel timeline)
    {
        double currentSeconds = timeline.Playback.CurrentTime.TotalSeconds;
        TimelineStructureDocument timelineStructure = timeline.Package.TimelineStructure;
        double songStartOffset = timelineStructure.GetSongStartOffset();
        double videoOffset = timeline.VideoOffset;
        return Math.Max(0, currentSeconds + songStartOffset + videoOffset);
    }

    private double GetEstimatedStreamVideoSeconds()
    {
        if (!_streamClock.IsRunning)
            return _streamVideoStartSeconds;

        return _streamVideoStartSeconds + _streamClock.Elapsed.TotalSeconds;
    }

    private async Task ApplyFrameBytesOnUiThreadAsync(byte[] frameBytes, FfmpegVideoFrameInfo info, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested || _disposed)
                return;

            EnsureCurrentFrame(info);
            WriteableBitmap? bitmap = CurrentFrame;
            if (bitmap == null)
                return;

            CopyPixelsToBitmap(frameBytes, bitmap, info.OutputWidth, info.OutputHeight);
            CurrentFrameVersion++;
        }, DispatcherPriority.Render);
    }

    private void ClearFrameOnUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ClearFrameOnUiThread, DispatcherPriority.Render);
            return;
        }

        CurrentFrame = null;
        _currentFrameWidth = 0;
        _currentFrameHeight = 0;
        CurrentFrameVersion++;
    }

    private void EnsureCurrentFrame(FfmpegVideoFrameInfo info)
    {
        if (CurrentFrame != null && _currentFrameWidth == info.OutputWidth && _currentFrameHeight == info.OutputHeight)
            return;

        CurrentFrame = new WriteableBitmap(
            new PixelSize(info.OutputWidth, info.OutputHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        _currentFrameWidth = info.OutputWidth;
        _currentFrameHeight = info.OutputHeight;
    }

    private byte[] GetStillFrameBuffer(FfmpegVideoFrameInfo info)
    {
        int byteCount = checked(info.OutputWidth * info.OutputHeight * 4);
        if (_stillFrameBytes.Length < byteCount)
            _stillFrameBytes = new byte[byteCount];

        return _stillFrameBytes;
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

    private void CancelStillFrame()
    {
        lock (_stillFrameLock)
        {
            _requestedStillFrameSeconds = double.NaN;
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
        lock (_stillFrameLock)
        {
            return _requestedStillFrameSeconds;
        }
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (ActiveTimeline != null)
            ActiveTimeline = null;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        CancelStillFrame();
        StopStream();

        ClearFrameOnUiThread();

        GC.SuppressFinalize(this);
    }
}
