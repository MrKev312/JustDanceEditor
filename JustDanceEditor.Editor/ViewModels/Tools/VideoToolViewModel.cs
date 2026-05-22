using Avalonia.Media.Imaging;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Video Preview", "View/Preview")]
public partial class VideoToolViewModel : TimelineToolViewModel, IDisposable
{
    private const double RestartStreamDriftSeconds = 0.45;
    private const double StillFrameToleranceSeconds = 0.04;

    private readonly FfmpegVideoFrameReader _frameReader = new();
    private readonly Stopwatch _streamClock = new();

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _stillFrameCts;
    private Task? _streamTask;
    private FfmpegVideoFrameInfo? _videoInfo;
    private string? _loadedVideoPath;
    private string? _streamVideoPath;
    private double _streamVideoStartSeconds;
    private double _lastStillFrameSeconds = double.NaN;
    private bool _disposed;

    [ObservableProperty]
    public partial Bitmap? CurrentFrame { get; set; }

    [ObservableProperty]
    public partial string VideoStatusText { get; set; } = "No active timeline";

    partial void OnCurrentFrameChanging(Bitmap? oldValue, Bitmap? newValue)
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
        SetFrameOnUiThread(null);
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
        SetFrameOnUiThread(null);

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
                    frame => SetFrameOnUiThread(frame, token),
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

        CancelStillFrame();
        _stillFrameCts = new CancellationTokenSource();
        CancellationToken token = _stillFrameCts.Token;
        string videoPath = _loadedVideoPath;
        FfmpegVideoFrameInfo info = _videoInfo;
        _lastStillFrameSeconds = targetSeconds;

        _ = Task.Run(async () =>
        {
            try
            {
                Bitmap frame = await _frameReader.ReadFrameAsync(videoPath, targetSeconds, info, token);
                SetFrameOnUiThread(frame, token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        VideoStatusText = string.Empty;
                });
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
        }, token);
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

    private void SetFrameOnUiThread(Bitmap? frame, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                frame?.Dispose();
                return;
            }

            CurrentFrame = frame;
        }, DispatcherPriority.Render);
    }

    private void CancelStillFrame()
    {
        _stillFrameCts?.Cancel();
        _stillFrameCts?.Dispose();
        _stillFrameCts = null;
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

        CurrentFrame = null;

        GC.SuppressFinalize(this);
    }
}
