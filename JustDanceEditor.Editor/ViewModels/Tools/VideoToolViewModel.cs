using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;

using LibVLCSharp.Shared;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Video Preview", "General")]
public partial class VideoToolViewModel : TimelineToolViewModel, IDisposable
{
    public MediaPlayer MediaPlayer { get; }
    private TimelineEditorViewModel? _lastTimeline;

    public VideoToolViewModel()
    {
        if (Avalonia.Application.Current is App app)
        {
            MediaPlayer = new MediaPlayer(app.LibVLC) { Mute = true };
        }
        else
        {
            // Fallback for designer or tests
            MediaPlayer = new MediaPlayer(new LibVLC()) { Mute = true };
        }

        SyncMedia();
    }

    protected override void HandleActiveTimelineChanged(TimelineEditorViewModel? value)
    {
        SyncMedia();
    }

    private void SyncMedia()
    {
        // MediaPlayer might not be initialized yet if called from base constructor
        if (MediaPlayer == null)
            return;

        // Unsubscribe from previous
        if (_lastTimeline != null)
        {
            _lastTimeline.Playback.TimeChanged -= Playback_TimeChanged;
            _lastTimeline.Playback.PlayStateChanged -= Playback_PlayStateChanged;
        }

        if (ActiveTimeline != null && !string.IsNullOrEmpty(ActiveTimeline.VideoPath))
        {
            var app = (App)Avalonia.Application.Current!;
            
            // Re-use or create media
            if (MediaPlayer.Media == null || MediaPlayer.Media.Mrl != ActiveTimeline.VideoPath)
            {
                MediaPlayer.Media = new Media(app.LibVLC, ActiveTimeline.VideoPath, FromType.FromPath);
            }
            
            // Initial sync
            SyncTime();

            // Subscribe to timing changes
            ActiveTimeline.Playback.TimeChanged += Playback_TimeChanged;
            ActiveTimeline.Playback.PlayStateChanged += Playback_PlayStateChanged;
            _lastTimeline = ActiveTimeline;
            
            if (ActiveTimeline.Playback.IsPlaying)
            {
                // Delay play slightly to ensure VideoView has time to process the MediaPlayer assignment
                _ = Task.Delay(200).ContinueWith(_ => {
                     Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                         if (ActiveTimeline?.Playback.IsPlaying == true)
                            MediaPlayer.Play();
                     });
                });
            }
        }
        else
        {
            MediaPlayer.Media = null;
            _lastTimeline = null;
        }
    }

    private void Playback_PlayStateChanged(object? sender, EventArgs e)
    {
        if (ActiveTimeline?.Playback.IsPlaying == true)
        {
            if (MediaPlayer.State == VLCState.Ended)
            {
                MediaPlayer.Stop();
            }
            MediaPlayer.Play();
        }
        else
        {
            MediaPlayer.Pause();
        }
    }

    private void Playback_TimeChanged(object? sender, EventArgs e)
    {
        SyncTime();
    }

    private DateTime _lastSyncTime = DateTime.MinValue;

    private void SyncTime()
    {
        if (ActiveTimeline == null || MediaPlayer == null)
            return;

        bool editorIsPlaying = ActiveTimeline.Playback.IsPlaying;

        // Throttle synchronization while playing to prevent UI thread saturation and jitter.
        // VLC's internal clock is stable enough that we only need to check for drift every ~250ms.
        if (editorIsPlaying && (DateTime.UtcNow - _lastSyncTime).TotalMilliseconds < 250)
            return;

        _lastSyncTime = DateTime.UtcNow;

        double currentSeconds = ActiveTimeline.Playback.CurrentTime.TotalSeconds;
        double startOffset = ActiveTimeline.TimelineStructure.GetSongStartOffset();
        
        double targetSeconds = currentSeconds + startOffset + ActiveTimeline.VideoOffset;
        long targetMs = (long)(targetSeconds * 1000);
        
        // Use a much larger tolerance when playing (drift check) than when scrubbing (frame precision)
        long tolerance = editorIsPlaying ? 500 : 50;
        long diff = Math.Abs(MediaPlayer.Time - targetMs);

        if (diff > tolerance)
        {
            if (MediaPlayer.State == VLCState.Ended)
            {
                MediaPlayer.Stop();
            }
            
            // Re-start if it was stopped/finished
            if (MediaPlayer.State == VLCState.Stopped || MediaPlayer.State == VLCState.NothingSpecial)
            {
                MediaPlayer.Play();
            }

            MediaPlayer.Time = Math.Max(0, targetMs);
        }

        // Final state enforcement: only call if there is a mismatch
        var vlcState = MediaPlayer.State;

        if (editorIsPlaying)
        {
            if (vlcState != VLCState.Playing && vlcState != VLCState.Buffering)
            {
                MediaPlayer.Play();
            }
        }
        else
        {
            // Use Pause() to ensure it stays on the frame during scrubbing
            if (vlcState != VLCState.Paused && vlcState != VLCState.Stopped && vlcState != VLCState.NothingSpecial)
            {
                MediaPlayer.Pause();
            }
        }
    }

    public void Dispose()
    {
        if (_lastTimeline != null)
        {
            _lastTimeline.Playback.TimeChanged -= Playback_TimeChanged;
            _lastTimeline.Playback.PlayStateChanged -= Playback_PlayStateChanged;
        }

        MediaPlayer?.Dispose();
    }
}