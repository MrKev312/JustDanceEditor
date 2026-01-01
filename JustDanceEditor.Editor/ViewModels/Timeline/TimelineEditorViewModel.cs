// File: .\ViewModels\Timeline\TimelineEditorViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class TimelineEditorViewModel : Document, JustDanceEditor.Editor.Services.ILyricsColorService
{
    private readonly IntermediateSongPackage _package;

    public string RootPath { get; }

    /// <summary>
    /// Gets the undo/redo service for this timeline.
    /// </summary>
    public IUndoService UndoService { get; }

    [ObservableProperty]
    public partial double PixelsPerBeat { get; set; } = 100.0;

    [ObservableProperty]
    public partial double ZoomPercentage { get; set; } = 100.0;

    [ObservableProperty]
    public partial double MinZoomPercentage { get; set; } = 10.0;

    [ObservableProperty]
    public partial double MaxZoomPercentage { get; set; } = 10000.0;

    [ObservableProperty]
    public partial double ScrollOffsetX { get; set; } = 0.0;

    [ObservableProperty]
    public partial double CurrentBeat { get; set; } = 0.0;

    [ObservableProperty]
    public partial int BeatOffset { get; set; } = 0;

    [ObservableProperty]
    public partial double MaxBeat { get; set; } = 500.0;

    [ObservableProperty]
    public partial double TimelineWidth { get; set; } = 0.0;

    [ObservableProperty]
    public partial float[] WaveformSamples { get; set; } = [];

    // Snapping options (can be bound to UI toggles)
    [ObservableProperty]
    public partial bool SnapToGrid { get; set; } = false;

    [ObservableProperty]
    public partial bool SnapToCurrentTimeMarker { get; set; } = false;

    [ObservableProperty]
    public partial double SnapGridSize { get; set; } = 1.0;

    [ObservableProperty]
    public partial double SnapThreshold { get; set; } = 0.25;

    [ObservableProperty]
    public partial bool SnapToClips { get; set; } = false;

    // Static synchronization for snapping across all timeline instances
    private static bool _globalSnapToGrid = false;
    private static event Action<bool>? SnapToGridChangedGlobal;

    private static bool _globalSnapToPlayhead = false;
    private static event Action<bool>? SnapToPlayheadChangedGlobal;

    private static double _globalSnapGridSize = 1.0;
    private static event Action<double>? SnapGridSizeChangedGlobal;

    private static double _globalSnapThreshold = 0.25;
    private static event Action<double>? SnapThresholdChangedGlobal;

    private static bool _globalSnapToClips = false;
    private static event Action<bool>? SnapToClipsChangedGlobal;

    // suppress broadcasting when applying remote changes
    private bool _suppressSnapBroadcast = false;

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];
    public IPlaybackService Playback { get; }
    public TimelineStructureDocument TimelineStructure => _package.TimelineStructure;

    public string AudioPath { get; private set; } = "";
    public string VideoPath { get; private set; } = "";
    public string PreparedAudioPath { get; private set; } = "";
    public double StartBeatValue { get; private set; }
    public double VideoOffset { get; private set; }
    public int CoachCount => _package.Metadata.CoachCount;
    public string LyricsColor => _package.Metadata.LyricsColor;

    public IEnumerable<string> AvailableHandCoachMoves => _package.HandCoachMoves.Keys;
    public IEnumerable<string> AvailableFullBodyCoachMoves => _package.FullBodyCoachMoves.Keys;

    /// <summary>
    /// Attempt to retrieve a color associated with the given move id from the package coach move definitions.
    /// Returns true if a color was found and parsed.
    /// </summary>
    public bool TryGetCoachMoveColor(string moveId, out Color color)
    {
        CoachMoveDefinition? def = null;
        if (_package.HandCoachMoves.TryGetValue(moveId, out var d) || _package.FullBodyCoachMoves.TryGetValue(moveId, out d))
            def = d;

        if (def != null && Color.TryParse(def.Color, out var c))
        {
            color = c;
            return true;
        }

        color = Colors.LightGray;
        return false;
    }
    public IEnumerable<string> AvailablePictograms
    {
        get
        {
            var dir = Path.Combine(RootPath, "assets", "pictograms");
            if (!Directory.Exists(dir))
                return [];
            return Directory.GetFiles(dir, "*.*")
                 .Select(Path.GetFileNameWithoutExtension)
                 .Where(x => !string.IsNullOrEmpty(x))
                 .OrderBy(x => x)!;
        }
    }

    // Simple undo/redo stack
    private readonly Stack<(Action Undo, Action Redo)> _undoStack = new();
    private readonly Stack<(Action Undo, Action Redo)> _redoStack = new();

    public TimelineEditorViewModel(IntermediateSongPackage package, string rootPath)
    {
        _package = package;
        RootPath = rootPath;
        Id = package.Metadata.SongID.ToString();
        Title = package.Metadata.MapName;

        // Create undo service for this timeline
        UndoService = new UndoService();

        Playback = new PlaybackService();
        Playback.TimeChanged += (s, e) => CurrentBeat = Playback.CurrentBeat;

        BeatOffset = _package.TimelineStructure.StartBeat;
        MaxBeat = _package.TimelineStructure.EndBeat - _package.TimelineStructure.StartBeat;
        UpdateTimelineWidth();

        BuildTimeline();
        _ = InitializeMedia();
        SnapToGrid = _globalSnapToGrid;
        SnapToCurrentTimeMarker = _globalSnapToPlayhead;
        SnapGridSize = _globalSnapGridSize;
        SnapThreshold = _globalSnapThreshold;
        SnapToClips = _globalSnapToClips;

        SnapToGridChangedGlobal += (v) =>
        {
            if (v == SnapToGrid)
                return;
            _suppressSnapBroadcast = true;
            SnapToGrid = v;
        };

        SnapToPlayheadChangedGlobal += (v) =>
        {
            if (v == SnapToCurrentTimeMarker)
                return;
            _suppressSnapBroadcast = true;
            SnapToCurrentTimeMarker = v;
        };

        SnapGridSizeChangedGlobal += (v) =>
        {
            if (Math.Abs(v - SnapGridSize) < 1e-9)
                return;
            _suppressSnapBroadcast = true;
            SnapGridSize = v;
        };

        SnapThresholdChangedGlobal += (v) =>
        {
            if (Math.Abs(v - SnapThreshold) < 1e-9)
                return;
            _suppressSnapBroadcast = true;
            SnapThreshold = v;
        };

        SnapToClipsChangedGlobal += (v) =>
        {
            if (v == SnapToClips)
                return;
            _suppressSnapBroadcast = true;
            SnapToClips = v;
        };
    }

    private async Task InitializeMedia()
    {
        AudioPath = Path.Combine(RootPath, IntermediatePackageLayout.Assets.AudioMasterFile);
        string videoDir = Path.Combine(RootPath, IntermediatePackageLayout.Assets.VideoFolder);
        VideoPath = "";

        if (Directory.Exists(videoDir))
        {
            var files = Directory.GetFiles(videoDir, "*.webm");
            if (files.Length > 0)
            {
                // Select the largest file (highest quality heuristic)
                VideoPath = files
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.Length)
                    .First()
                    .FullName;
            }
        }

        StartBeatValue = _package.TimelineStructure.StartBeat;
        VideoOffset = -_package.TimelineStructure.VideoStartOffset;

        // Prepare audio (Opus -> WAV)
        if (File.Exists(AudioPath))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JustDanceEditor");
            Directory.CreateDirectory(tempDir);
            PreparedAudioPath = Path.Combine(tempDir, $"{Id}_{Guid.NewGuid():N}.wav");

            IConversion conversion = await FFmpeg.Conversions.FromSnippet.Convert(AudioPath, PreparedAudioPath);
            await conversion.Start();
        }

        // Marker-based timing logic
        TimelineStructureDocument ts = _package.TimelineStructure;
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
        // Determine lyrics color once
        Color lyricsColor = Colors.Yellow;
        if (!string.IsNullOrEmpty(_package.Metadata.LyricsColor))
        {
            lyricsColor = ClipViewModel.ParseRgbaHex(_package.Metadata.LyricsColor);
        }

        // Helper-local to capture lyricsColor where needed
        void AddTrack(string title, double height, Color color, IEnumerable<TimelineClipBase> clips, bool isFullBody = false)
        {
            var track = new TrackViewModel() { Title = title, Height = height, TrackColor = color };
            foreach (TimelineClipBase clip in clips)
            {
                switch (clip)
                {
                    case KaraokeClip kc:
                        track.Clips.Add(new KaraokeClipViewModel(kc, kc.Duration, lyricsColor, kc.Lyrics, RootPath, this));
                        break;
                    case PictogramClip pc:
                        track.Clips.Add(new PictogramClipViewModel(pc, pc.Duration, Colors.LightBlue, pc.PictogramId, RootPath, this));
                        break;
                    case MoveClip mc:
                        {
                            Color moveColor = Colors.LightGray;
                            double duration = 24;
                            string name = mc.MoveId;
                            if (_package.HandCoachMoves.TryGetValue(mc.MoveId, out CoachMoveDefinition? def) || _package.FullBodyCoachMoves.TryGetValue(mc.MoveId, out def))
                            {
                                if (Color.TryParse(def.Color, out Color c))
                                    moveColor = c;
                                duration = def.Duration;
                            }
                            track.Clips.Add(new MoveClipViewModel(mc, duration, moveColor, name, RootPath, this, isFullBody));
                            break;
                        }
                    case GoldEffectClip gc:
                        track.Clips.Add(new GoldEffectClipViewModel(gc, gc.Duration, Colors.Gold, "Gold Effect", RootPath, this));
                        break;
                    default:
                        // Unknown clip type - fallback to GoldEffect wrapper
                        track.Clips.Add(new GoldEffectClipViewModel(new GoldEffectClip(), 0, Colors.LightGray, clip.GetType().Name, RootPath, this));
                        break;
                }
            }
            Tracks.Add(track);
        }

        // Build tracks using configuration-style calls (keeps BuildTimeline concise)
        AddTrack("Lyrics", 40, Colors.Goldenrod, _package.Lyrics.Clips.Cast<TimelineClipBase>());
        AddTrack("Pictograms", 60, Colors.CornflowerBlue, _package.Pictograms.Clips.Cast<TimelineClipBase>());

        // One track per coach timeline to preserve coach id in title
        foreach (MoveTimeline coachTimeline in _package.CoachTimelines)
            AddTrack($"Coach {coachTimeline.CoachId}", 40, Colors.MediumPurple, coachTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: false);

        foreach (MoveTimeline fullBodyTimeline in _package.FullBodyCoachTimelines)
            AddTrack($"FullBody Coach {fullBodyTimeline.CoachId}", 60, Colors.SeaGreen, fullBodyTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: true);

        AddTrack("Gold Effects", 30, Colors.OrangeRed, _package.GoldEffects.Clips.Cast<TimelineClipBase>());
    }

    private void UpdateTimelineWidth()
    {
        TimelineWidth = MaxBeat * PixelsPerBeat;
    }

    partial void OnPixelsPerBeatChanged(double value)
    {
        UpdateTimelineWidth();
        ZoomPercentage = value;
        OnPropertyChanged(nameof(ZoomPercentage));
    }

    partial void OnZoomPercentageChanged(double value)
    {
        if (value < MinZoomPercentage)
            value = MinZoomPercentage;
        if (value > MaxZoomPercentage)
            value = MaxZoomPercentage;
        PixelsPerBeat = value;
    }

    partial void OnMinZoomPercentageChanged(double value)
    {
        if (ZoomPercentage < value)
        {
            ZoomPercentage = value;
        }
    }

    public void FitToView(double viewportWidth)
    {
        if (MaxBeat <= 0)
            return;

        // Calculate the zoom level that fits the whole song
        double fitPpb = viewportWidth / MaxBeat;
        MinZoomPercentage = fitPpb;
        ZoomPercentage = fitPpb;
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (Playback.IsPlaying)
            Playback.Pause();
        else
            Playback.Play();
    }

    [RelayCommand]
    private void Undo()
    {
        UndoService.Undo();
    }

    [RelayCommand]
    private void Redo()
    {
        UndoService.Redo();
    }

    [RelayCommand]
    public void DeleteSelectedClips()
    {
        var toDelete = new List<(TrackViewModel Track, ClipViewModel Clip)>();
        foreach (TrackViewModel track in Tracks)
        {
            foreach (ClipViewModel clip in track.Clips)
            {
                if (clip.IsSelected)
                {
                    toDelete.Add((track, clip));
                }
            }
        }

        if (toDelete.Count == 0)
            return;

        PushUndo(
            undo: () =>
            {
                foreach ((TrackViewModel Track, ClipViewModel Clip) in toDelete)
                {
                    if (!Track.Clips.Contains(Clip))
                        Track.Clips.Add(Clip);
                }
            },
            redo: () =>
            {
                foreach ((TrackViewModel Track, ClipViewModel Clip) in toDelete)
                {
                    Track.Clips.Remove(Clip);
                }
            }
        );

        // Execute
        foreach ((TrackViewModel Track, ClipViewModel Clip) in toDelete)
        {
            Track.Clips.Remove(Clip);
        }
    }

    public void PushUndo(Action undo, Action redo)
    {
        if (undo == null || redo == null)
            return;
        UndoService.Record(undo, redo);
    }

    /// <summary>
    /// Updates the lyrics color in the metadata and triggers rerender.
    /// Note: This does NOT record undo/redo. Call PushUndo separately if needed.
    /// </summary>
    public void UpdateLyricsColor(string rgbaHexColor)
    {
        _package.Metadata.LyricsColor = rgbaHexColor;
        // Trigger any UI updates that depend on LyricsColor
        OnPropertyChanged(nameof(LyricsColor));
    }

    public string GetLyricsColor() => LyricsColor;

    public override bool OnClose()
    {
        Playback.Dispose();
        if (!string.IsNullOrEmpty(PreparedAudioPath) && File.Exists(PreparedAudioPath))
        {
            try
            {
                File.Delete(PreparedAudioPath);
            }
            catch { }
        }

        return base.OnClose();
    }

    partial void OnSnapToGridChanged(bool value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _globalSnapToGrid = value;
        SnapToGridChangedGlobal?.Invoke(value);
    }

    partial void OnSnapToCurrentTimeMarkerChanged(bool value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _globalSnapToPlayhead = value;
        SnapToPlayheadChangedGlobal?.Invoke(value);
    }

    partial void OnSnapGridSizeChanged(double value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _globalSnapGridSize = value;
        SnapGridSizeChangedGlobal?.Invoke(value);
    }

    partial void OnSnapThresholdChanged(double value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _globalSnapThreshold = value;
        SnapThresholdChangedGlobal?.Invoke(value);
    }

    partial void OnSnapToClipsChanged(bool value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _globalSnapToClips = value;
        SnapToClipsChangedGlobal?.Invoke(value);
    }
}