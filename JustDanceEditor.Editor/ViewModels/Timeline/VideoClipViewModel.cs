using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.ComponentModel;
using System.IO;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public sealed class VideoClipViewModel : ClipViewModel
{
    private readonly VideoTimelineClip _videoClip;
    private readonly double _durationSeconds;

    public VideoClipViewModel(double durationSeconds, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(new VideoTimelineClip(), Colors.IndianRed, "Video", rootPath, parentTimeline)
    {
        _videoClip = (VideoTimelineClip)RawClip;
        _durationSeconds = Math.Max(0, durationSeconds);
        RefreshFromTimeline();
    }

    public override string Name => !string.IsNullOrWhiteSpace(_parentTimeline?.VideoPath)
        ? Path.GetFileName(_parentTimeline.VideoPath)
        : "Video";

    protected override int GetDurationFrames() => _videoClip.Duration;

    protected override void SetDurationFrames(int frames)
    {
    }

    protected override void SetStartFrames(int frames)
    {
        if (_parentTimeline == null)
        {
            _videoClip.StartTime = frames;
            return;
        }

        _parentTimeline.SetVideoOffsetFromClipStartBeat(frames / 24d);
    }

    protected override void OnStartBeatChanged(double oldStartBeat, double newStartBeat)
    {
        RefreshFromTimeline();
    }

    public void RefreshFromTimeline()
    {
        if (_parentTimeline == null)
            return;

        double songStartOffset = _parentTimeline.TimelineStructure.GetSongStartOffset();
        double playbackStartSeconds = -(songStartOffset + _parentTimeline.VideoOffset);
        double playbackEndSeconds = playbackStartSeconds + _durationSeconds;

        double startBeat = _parentTimeline.GetBeatLabelAtPlaybackSeconds(playbackStartSeconds);
        double endBeat = _parentTimeline.GetBeatLabelAtPlaybackSeconds(playbackEndSeconds);

        int startFrames = (int)(startBeat * 24d);
        int durationFrames = Math.Max(1, (int)(Math.Max(0, endBeat - startBeat) * 24d));

        bool startChanged = _videoClip.StartTime != startFrames;
        bool durationChanged = _videoClip.Duration != durationFrames;
        bool nameMayHaveChanged = true;

        _videoClip.StartTime = startFrames;
        _videoClip.Duration = durationFrames;

        if (startChanged)
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(StartBeat)));

        if (durationChanged)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(DurationBeats)));
            NotifyClipDataChanged(nameof(DurationBeats));
        }

        if (nameMayHaveChanged)
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
    }

    private sealed class VideoTimelineClip : TimelineClipBase
    {
        public int Duration { get; set; }
    }
}