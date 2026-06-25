using Avalonia.Media.Imaging;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.IO;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Video Preview", "View/Preview")]
public partial class VideoToolViewModel : TimelineToolViewModel, IDisposable
{
    private SharedVideoPreviewLease? _sessionLease;
    private SharedVideoPreviewSession? _session;
    private string? _loadedVideoPath;
    private bool _disposed;

    [ObservableProperty]
    public partial WriteableBitmap? CurrentFrame { get; set; }

    [ObservableProperty]
    public partial int CurrentFrameVersion { get; set; }

    [ObservableProperty]
    public partial string VideoStatusText { get; set; } = "No active timeline";

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        SyncMedia();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        DetachSession("No active timeline");
    }

    protected override void OnTimelinePropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(TimelineEditorViewModel.VideoPath))
        {
            SyncMedia();
            return;
        }

        if (propertyName == nameof(TimelineEditorViewModel.VideoOffset))
            RequestFrame();
    }

    protected override void OnTimeChanged()
    {
        RequestFrame();
    }

    private void SyncMedia()
    {
        if (_disposed)
            return;

        string? videoPath = ActiveTimeline?.VideoPath;
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            DetachSession(ActiveTimeline == null ? "No active timeline" : "No video found");
            return;
        }

        string normalizedPath = Path.GetFullPath(videoPath);
        if (string.Equals(normalizedPath, _loadedVideoPath, PathComparison))
        {
            RequestFrame();
            return;
        }

        DetachSession("Loading video...");

        _loadedVideoPath = normalizedPath;
        _sessionLease = SharedVideoPreviewSessions.Acquire(normalizedPath);
        _session = _sessionLease.Session;
        _session.StateChanged += Session_StateChanged;

        ApplySessionState(_session);
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_disposed || ActiveTimeline == null || _session == null)
            return;

        double targetSeconds = GetTargetVideoSeconds(ActiveTimeline);
        _session.RequestFrame(targetSeconds, ActiveTimeline.Playback.IsPlaying);
    }

    private void Session_StateChanged(object? sender, EventArgs e)
    {
        if (sender is SharedVideoPreviewSession session)
            ApplySessionState(session);
    }

    private void ApplySessionState(SharedVideoPreviewSession session)
    {
        if (!ReferenceEquals(session, _session) || _disposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplySessionState(session), DispatcherPriority.Render);
            return;
        }

        if (!ReferenceEquals(session, _session) || _disposed)
            return;

        CurrentFrame = session.CurrentFrame;
        CurrentFrameVersion = session.FrameVersion;
        VideoStatusText = session.StatusText;
    }

    private void DetachSession(string statusText)
    {
        if (_session != null)
            _session.StateChanged -= Session_StateChanged;

        _session = null;
        _loadedVideoPath = null;
        CurrentFrame = null;
        CurrentFrameVersion++;
        VideoStatusText = statusText;

        _sessionLease?.Dispose();
        _sessionLease = null;
    }

    private static double GetTargetVideoSeconds(TimelineEditorViewModel timeline)
    {
        double currentSeconds = timeline.Playback.CurrentTime.TotalSeconds;
        TimelineStructureDocument timelineStructure = timeline.Package.TimelineStructure;
        double songStartOffset = timelineStructure.GetSongStartOffset();
        double videoOffset = timeline.VideoOffset;
        return Math.Max(0, currentSeconds + songStartOffset + videoOffset);
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (ActiveTimeline != null)
            ActiveTimeline = null;

        DetachSession("No active timeline");
        GC.SuppressFinalize(this);
    }
}