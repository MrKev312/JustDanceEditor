// File: .\ViewModels\Timeline\TimelineEditorViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class TimelineEditorViewModel : Document
{
    private readonly IntermediateSongPackage _package;
    private readonly string _rootPath;

    [ObservableProperty] private double _pixelsPerBeat = 50.0;
    [ObservableProperty] private double _scrollOffsetX = 0.0;
    [ObservableProperty] private double _currentBeat = 0.0;
    [ObservableProperty] private int _beatOffset = 0;
    [ObservableProperty] private double _maxBeat = 500.0;
    [ObservableProperty] private double _timelineWidth = 0.0;
    [ObservableProperty] private float[] _waveformSamples = [];

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];
    public IPlaybackService Playback { get; }
    public TimelineStructureDocument TimelineStructure => _package.TimelineStructure;

    public string AudioPath { get; private set; } = "";
    public string VideoPath { get; private set; } = "";
    public string PreparedAudioPath { get; private set; } = "";
    public double StartBeatValue { get; private set; }
    public double VideoOffset { get; private set; }

    public TimelineEditorViewModel(IntermediateSongPackage package, string rootPath)
    {
        _package = package;
        _rootPath = rootPath;
        Id = package.Metadata.SongID.ToString();
        Title = package.Metadata.MapName;

        Playback = new PlaybackService();
        Playback.TimeChanged += (s, e) => CurrentBeat = Playback.CurrentBeat;

        BeatOffset = _package.TimelineStructure.StartBeat;
        MaxBeat = _package.TimelineStructure.EndBeat - _package.TimelineStructure.StartBeat;
        UpdateTimelineWidth();

        BuildTimeline();
        _ = InitializeMedia();
    }

    private async Task InitializeMedia()
    {
        AudioPath = Path.Combine(_rootPath, IntermediatePackageLayout.Assets.AudioMasterFile);
        string videoDir = Path.Combine(_rootPath, IntermediatePackageLayout.Assets.VideoFolder);
        VideoPath = "";

        if (Directory.Exists(videoDir))
        {
            var files = Directory.GetFiles(videoDir, "*.webm");
            if (files.Length > 0)
                VideoPath = files[0];
        }

        StartBeatValue = _package.TimelineStructure.StartBeat;
        VideoOffset = -_package.TimelineStructure.VideoStartOffset;

        // Prepare audio (Opus -> WAV)
        if (File.Exists(AudioPath))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JustDanceEditor");
            Directory.CreateDirectory(tempDir);
            PreparedAudioPath = Path.Combine(tempDir, $"{Id}_{Guid.NewGuid():N}.wav");

            var conversion = await Xabe.FFmpeg.FFmpeg.Conversions.FromSnippet.Convert(AudioPath, PreparedAudioPath);
            await conversion.Start();
        }

        // Marker-based timing logic
        var ts = _package.TimelineStructure;
        double startOffset = ts.GetSongStartOffset();

        // Ensure this timeline is loaded
        await Playback.LoadMediaAsync(
            PreparedAudioPath, 
            VideoPath, 
            b => ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(b)),
            s => ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(s)),
            VideoOffset);

        WaveformSamples = await AudioWaveformService.GetWaveformDataAsync(AudioPath);
    }

    private void BuildTimeline()
    {
        // 1. Lyrics
        TrackViewModel lyricsTrack = new() { Title = "Lyrics", Height = 40, TrackColor = Colors.Goldenrod };
        foreach (KaraokeClip clip in _package.Lyrics.Clips)
        {
            lyricsTrack.Clips.Add(new ClipViewModel(clip, clip.Duration, Colors.Yellow, clip.Lyrics, _rootPath));
        }

        Tracks.Add(lyricsTrack);

        // 2. Pictograms
        TrackViewModel pictoTrack = new() { Title = "Pictograms", Height = 60, TrackColor = Colors.CornflowerBlue };
        foreach (PictogramClip clip in _package.Pictograms.Clips)
        {
            pictoTrack.Clips.Add(new ClipViewModel(clip, clip.Duration, Colors.LightBlue, clip.PictogramId, _rootPath));
        }

        Tracks.Add(pictoTrack);

        // 3. Coaches
        foreach (MoveTimeline coachTimeline in _package.CoachTimelines)
        {
            TrackViewModel coachTrack = new() { Title = $"Coach {coachTimeline.CoachId}", Height = 40, TrackColor = Colors.MediumPurple };
            foreach (MoveClip clip in coachTimeline.Clips)
            {
                Color color = Colors.LightGray;
                double duration = 24;
                if (_package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? def))
                {
                    if (Color.TryParse(def.Color, out Color c))
                        color = c;
                    duration = def.Duration;
                }

                coachTrack.Clips.Add(new ClipViewModel(clip, duration, color, clip.MoveId, _rootPath));
            }

            Tracks.Add(coachTrack);
        }

        // 4. Gold Moves
        TrackViewModel goldTrack = new() { Title = "Gold Effects", Height = 30, TrackColor = Colors.OrangeRed };
        foreach (GoldEffectClip clip in _package.GoldEffects.Clips)
        {
            goldTrack.Clips.Add(new ClipViewModel(clip, clip.Duration, Colors.Gold, "Gold Effect", _rootPath));
        }

        Tracks.Add(goldTrack);
    }

    private void UpdateTimelineWidth()
    {
        TimelineWidth = MaxBeat * PixelsPerBeat;
    }

    partial void OnPixelsPerBeatChanged(double value)
    {
        UpdateTimelineWidth();
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (Playback.IsPlaying)
            Playback.Pause();
        else
            Playback.Play();
    }

    public override bool OnClose()
    {
        Playback.Dispose();
        if (!string.IsNullOrEmpty(PreparedAudioPath) && File.Exists(PreparedAudioPath))
        {
            try { File.Delete(PreparedAudioPath); } catch { }
        }

        return base.OnClose();
    }
}