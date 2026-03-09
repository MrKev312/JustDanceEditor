using Avalonia;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using LibVLCSharp.Shared;

using System;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Video Preview", "View/Preview")]
public partial class VideoToolViewModel : TimelineToolViewModel, IDisposable
{
    private LibVLC LibVLC
    {
        get
        {
            if (field == null)
                field = new LibVLC();

            return field;
        }
    }
    private string? _loadedVideoPath;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private bool _disposed;

    public MediaPlayer MediaPlayer { get; }

    public VideoToolViewModel()
    {
        // Resolve LibVLC from the DI container set up in App.axaml.cs.
        // Activator.CreateInstance (used by the Dock framework) requires a parameterless ctor.
        if (Application.Current is App app)
        {
            LibVLC = app.LibVLC;
        }
        else
        {
            // Design-time / test fallback — LibVLC may not be available
            LibVLC = new LibVLC();
        }

        MediaPlayer = new MediaPlayer(LibVLC) { Mute = true };
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        SyncMedia();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        _loadedVideoPath = null;
        MediaPlayer.Media = null;
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
        if (videoPath == _loadedVideoPath)
            return;

        _loadedVideoPath = videoPath;

        if (!string.IsNullOrEmpty(videoPath))
            MediaPlayer?.Media = new Media(LibVLC, videoPath, FromType.FromPath);
        else
            MediaPlayer.Media = null;
    }

    private void SyncTime()
    {
        if (MediaPlayer.Media == null || ActiveTimeline == null)
            return;

        bool isPlaying = ActiveTimeline.Playback.IsPlaying;

        // Throttle VLC seeks while playing — its internal clock is stable enough
        if (isPlaying && (DateTime.UtcNow - _lastSyncTime).TotalMilliseconds < 250)
            return;

        _lastSyncTime = DateTime.UtcNow;

        double currentSeconds = ActiveTimeline.Playback.CurrentTime.TotalSeconds;
        TimelineStructureDocument ts = ActiveTimeline.Package.TimelineStructure;
        double songStartOffset = ts.GetSongStartOffset();
        double videoOffset = ActiveTimeline.VideoOffset;
        long targetMs = (long)((currentSeconds + songStartOffset + videoOffset) * 1000);
        targetMs = Math.Max(0, targetMs);

        long tolerance = isPlaying ? 500 : 50;
        long diff = Math.Abs(MediaPlayer.Time - targetMs);

        if (diff > tolerance)
        {
            if (MediaPlayer.State == VLCState.Ended)
                MediaPlayer.Stop();

            if (MediaPlayer.State is VLCState.Stopped or VLCState.NothingSpecial)
                MediaPlayer.Play();

            MediaPlayer.Time = targetMs;
        }

        VLCState vlcState = MediaPlayer.State;
        if (isPlaying)
        {
            if (vlcState is not VLCState.Playing and not VLCState.Buffering)
                MediaPlayer.Play();
        }
        else
        {
            if (vlcState is not VLCState.Paused and not VLCState.Stopped and not VLCState.NothingSpecial)
                MediaPlayer.Pause();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Detach from any active timeline first
        if (ActiveTimeline != null)
        {
            // The base class UnsubscribeFromTimeline handles event cleanup
            ActiveTimeline = null;
        }

        // Detach media before stopping/disposing to avoid a native crash
        MediaPlayer.Media = null;
        MediaPlayer.Stop();
        MediaPlayer.Dispose();

        GC.SuppressFinalize(this);
    }
}