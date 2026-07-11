using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineMediaController(TimelineEditorViewModel timeline)
{
    private static readonly string[] SupportedVideoExtensions = ["*.webm", "*.mp4", "*.mkv", "*.mov"];

    public async Task InitializeAsync()
    {
        timeline.IsMediaLoading = true;
        timeline.MediaLoadError = null;

        try
        {
            timeline.AudioPath = Path.Combine(timeline.RootPath, IntermediatePackageLayout.Assets.AudioMasterFile);
            string videoDir = Path.Combine(timeline.RootPath, IntermediatePackageLayout.Assets.VideoFolder);
            timeline.VideoPath = "";
            timeline.VideoDurationSeconds = 0;

            if (Directory.Exists(videoDir))
            {
                FileInfo? selectedVideo = SupportedVideoExtensions
                    .SelectMany(pattern => Directory.GetFiles(videoDir, pattern))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(fi => fi.Length)
                    .FirstOrDefault();

                if (selectedVideo != null)
                {
                    timeline.VideoPath = selectedVideo.FullName;
                    timeline.VideoDurationSeconds = await GetMediaDurationAsync(timeline.VideoPath);
                }
            }

            timeline.NotifyMediaPathChanged();

            timeline.StartBeatValue = timeline.Package.TimelineStructure.StartBeat;
            SetVideoOffset(-timeline.Package.TimelineStructure.VideoStartOffset, syncTrack: false);
            SyncVideoTrackClip();

            if (File.Exists(timeline.AudioPath))
                timeline.PreparedAudio = await AudioConversionService.DecodeToPcmAsync(timeline.AudioPath);

            await LoadPlaybackAndWaveformAsync(refreshWaveform: true);
        }
        catch (Exception ex)
        {
            timeline.MediaLoadError = $"Failed to load media: {ex.Message}";
        }
        finally
        {
            timeline.IsMediaLoading = false;
        }
    }

    public async Task RebuildFromPackageAsync()
    {
        timeline.Playback.Pause();

        timeline.BaseTitle = timeline.Package.Metadata.MapName;
        timeline.Title = timeline.UndoService.IsDirty ? $"{timeline.BaseTitle} *" : timeline.BaseTitle;
        timeline.BeatOffset = timeline.Package.TimelineStructure.StartBeat;
        timeline.MaxBeat = timeline.Package.TimelineStructure.EndBeat - timeline.Package.TimelineStructure.StartBeat;
        timeline.StartBeatValue = timeline.Package.TimelineStructure.StartBeat;
        timeline.VideoOffset = -timeline.Package.TimelineStructure.VideoStartOffset;
        timeline.UpdateTimelineWidth();

        timeline.NotifyRebuiltTimelineStructure();

        timeline.DisposeClips();
        timeline.Tracks.Clear();
        timeline.ClearMoveDefinitions();
        timeline.BuildTimeline();
        SyncVideoTrackClip();

        if (timeline.PreparedAudio != null)
            await LoadPlaybackAndWaveformAsync(refreshWaveform: true);
    }

    public double GetPlaybackSecondsAtBeatLabel(double beatLabel)
    {
        if (timeline.TimelineStructure.Markers.Count < 2)
            return timeline.TimelineStructure.GetIndexFromBeatLabel(beatLabel);

        return timeline.TimelineStructure.GetSecondsAtBeat(timeline.TimelineStructure.GetIndexFromBeatLabel(beatLabel));
    }

    public double GetBeatLabelAtPlaybackSeconds(double seconds)
    {
        if (timeline.TimelineStructure.Markers.Count < 2)
            return timeline.TimelineStructure.GetBeatLabelFromIndex(seconds);

        return timeline.TimelineStructure.GetBeatLabelFromIndex(timeline.TimelineStructure.GetBeatAtSeconds(seconds));
    }

    public void SetVideoOffsetFromClipStartBeat(double startBeat)
    {
        double playbackStartSeconds = GetPlaybackSecondsAtBeatLabel(startBeat);
        double songStartOffset = timeline.TimelineStructure.GetSongStartOffset();
        SetVideoOffset(-(playbackStartSeconds + songStartOffset));
    }

    public void SetVideoOffset(double offsetSeconds)
    {
        SetVideoOffset(offsetSeconds, syncTrack: true);
    }

    public void SyncVideoTrackClip()
    {
        TrackViewModel? videoTrack = timeline.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
        if (videoTrack == null)
            return;

        if (string.IsNullOrWhiteSpace(timeline.VideoPath) || timeline.VideoDurationSeconds <= 0)
        {
            videoTrack.Clips.Clear();
            return;
        }

        VideoClipViewModel? videoClip = videoTrack.Clips.OfType<VideoClipViewModel>().FirstOrDefault();
        if (videoClip == null)
        {
            videoTrack.Clips.Clear();
            videoTrack.Clips.Add(new VideoClipViewModel(timeline.VideoDurationSeconds, timeline.RootPath, timeline));
            return;
        }

        videoClip.RefreshFromTimeline();
    }

    private void SetVideoOffset(double offsetSeconds, bool syncTrack)
    {
        if (Math.Abs(timeline.VideoOffset - offsetSeconds) <= 1e-9
            && Math.Abs(timeline.Package.TimelineStructure.VideoStartOffset + offsetSeconds) <= 1e-9)
        {
            return;
        }

        timeline.Package.TimelineStructure.VideoStartOffset = -offsetSeconds;
        timeline.VideoOffset = offsetSeconds;
        timeline.NotifyVideoOffsetChanged();

        if (syncTrack)
            SyncVideoTrackClip();
    }

    private async Task LoadPlaybackAndWaveformAsync(bool refreshWaveform)
    {
        TimelineStructureDocument ts = timeline.Package.TimelineStructure;

        await timeline.Playback.LoadMediaAsync(
            timeline.PreparedAudio,
            b => ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(b)),
            s => ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(s)));

        timeline.UpdateMetronomeTiming();

        timeline.AudioStartBeat = ts.StartBeat;
        double durationSeconds = timeline.Playback.Duration.TotalSeconds;
        timeline.AudioEndBeat = durationSeconds > 0 && ts.Markers.Count >= 2
            ? ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(durationSeconds))
            : ts.EndBeat;

        double endSeconds = ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(ts.EndBeat));
        timeline.Playback.SetExtendedEnd(TimeSpan.FromSeconds(endSeconds));

        if (refreshWaveform)
            timeline.WaveformSamples = await AudioConversionService.GetWaveformDataAsync(timeline.AudioPath);
    }

    private static async Task<double> GetMediaDurationAsync(string mediaPath)
    {
        if (!File.Exists(mediaPath))
            return 0;

        await JdiFfmpegResolver.GetFfmpegPathAsync();
        IMediaInfo info = await FFmpeg.GetMediaInfo(mediaPath);
        IVideoStream? videoStream = info.VideoStreams.FirstOrDefault();
        return videoStream?.Duration.TotalSeconds ?? info.Duration.TotalSeconds;
    }
}
