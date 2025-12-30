using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
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
        if (MediaPlayer == null) return;

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
            MediaPlayer.Play();
        else
            MediaPlayer.Pause();
    }

    private void Playback_TimeChanged(object? sender, EventArgs e)
    {
        SyncTime();
    }

    private void SyncTime()
    {
        if (ActiveTimeline == null || MediaPlayer == null) return;
        
        // Marker-based sync:
        // Absolute song time = Current playback time + map's song start offset
        // Video time = Absolute song time + video specific offset
        
        double currentSeconds = ActiveTimeline.Playback.CurrentTime.TotalSeconds;
        double startOffset = ActiveTimeline.TimelineStructure.GetSongStartOffset();
        
        double targetSeconds = currentSeconds + startOffset + ActiveTimeline.VideoOffset;
        long targetMs = (long)(targetSeconds * 1000);
        
        if (Math.Abs(MediaPlayer.Time - targetMs) > 100)
        {
            MediaPlayer.Time = Math.Max(0, targetMs);
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