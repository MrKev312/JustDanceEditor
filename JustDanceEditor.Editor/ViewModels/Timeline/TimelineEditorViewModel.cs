// File: .\ViewModels\Timeline\TimelineEditorViewModel.cs
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class TimelineEditorViewModel : Document
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

    // Registry of known move definitions keyed by (id,isFullBody)
    private readonly Dictionary<(string id, bool isFullBody), MoveDefinitionViewModel> _moveDefinitions = [];
    public IReadOnlyDictionary<(string id, bool isFullBody), MoveDefinitionViewModel> MoveDefinitions => _moveDefinitions;

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
        if (_package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? d) || _package.FullBodyCoachMoves.TryGetValue(moveId, out d))
            def = d;

        if (def != null && Color.TryParse(def.Color, out Color c))
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
            string dir = Path.Combine(RootPath, "assets", "pictograms");
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
            string[] files = Directory.GetFiles(videoDir, "*.webm");
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
            string tempDir = Path.Combine(Path.GetTempPath(), "JustDanceEditor");
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

        // Store the lyrics definition color directly on the timeline as a Color field
        _lyricsDefinitionColor = new Color(255, lyricsColor.R, lyricsColor.G, lyricsColor.B);
        // Persist to metadata for consistency
        _package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(_lyricsDefinitionColor);
        // Ensure UI knows lyrics color changed
        OnPropertyChanged(nameof(LyricsColor));
        OnPropertyChanged(nameof(LyricsDefinitionColor));

        // Ensure MoveDefinitions registry is populated from the package coach move definitions
        try
        {
            // Hand coach moves
            foreach (KeyValuePair<string, CoachMoveDefinition> kv in _package.HandCoachMoves)
            {
                string id = kv.Key;
                CoachMoveDefinition def = kv.Value;
                MoveDefinitionViewModel md = new()
                {
                    Id = id,
                    IsFullBody = false,
                    DefaultDuration = def.Duration <= 0 ? 24.0 : def.Duration,
                };
                if (Color.TryParse(def.Color, out Color c))
                    md.Color = c;
                _moveDefinitions[(id, false)] = md;
            }

            // Full body coach moves
            foreach (KeyValuePair<string, CoachMoveDefinition> kv in _package.FullBodyCoachMoves)
            {
                string id = kv.Key;
                CoachMoveDefinition def = kv.Value;
                MoveDefinitionViewModel md = new()
                {
                    Id = id,
                    IsFullBody = true,
                    DefaultDuration = def.Duration <= 0 ? 24.0 : def.Duration,
                };
                if (Color.TryParse(def.Color, out Color c))
                    md.Color = c;
                _moveDefinitions[(id, true)] = md;
            }
        }
        catch { }

        // Helper-local to capture lyricsColor where needed
        void AddTrack(string title, double height, Color color, IEnumerable<TimelineClipBase> clips, bool isFullBody = false)
        {
            TrackViewModel track = new() { Title = title, Height = height, TrackColor = color };
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
                            string name = mc.MoveId;
                            MoveDefinitionViewModel defVm = GetOrRegisterMove(mc.MoveId, isFullBody);
                            Color moveColor = defVm.Color;
                            double duration = defVm.DefaultDuration;
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

    public MoveDefinitionViewModel GetOrRegisterMove(string moveId, bool isFullBody)
    {
        if (string.IsNullOrEmpty(moveId))
            return new MoveDefinitionViewModel { Id = moveId, IsFullBody = isFullBody };

        (string moveId, bool isFullBody) key = (moveId, isFullBody);
        if (_moveDefinitions.TryGetValue(key, out MoveDefinitionViewModel? def))
            return def;

        // Attempt to seed from package if possible
        Color color = Colors.LightGray;
        double duration = 24.0;
        try
        {
            if (_package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? d) || _package.FullBodyCoachMoves.TryGetValue(moveId, out d))
            {
                if (d != null)
                {
                    if (Color.TryParse(d.Color, out Color c))
                        color = c;
                    if (d.Duration > 0)
                        duration = d.Duration;
                }
            }
        }
        catch { }

        def = new MoveDefinitionViewModel
        {
            Id = moveId,
            IsFullBody = isFullBody,
            Color = color,
            DefaultDuration = duration
        };

        _moveDefinitions[key] = def;
        return def;
    }

    /// <summary>
    /// Persist current timeline state back into the intermediate package and write it to disk.
    /// Ensures shared definitions (moves, lyrics color) are synchronized into the package before saving.
    /// </summary>
    public void Save()
    {
        // Sync MoveDefinitions into package coach move dictionaries
        foreach (KeyValuePair<(string id, bool isFullBody), MoveDefinitionViewModel> kv in _moveDefinitions)
        {
            string id = kv.Key.id;
            bool isFull = kv.Key.isFullBody;
            MoveDefinitionViewModel def = kv.Value;

            CoachMoveDefinition coachDef = new()
            {
                Color = ClipViewModel.ColorToRgbaHex(def.Color),
                Duration = (int)def.DefaultDuration,
                MoveType = isFull ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking
            };

            if (isFull)
                _package.FullBodyCoachMoves[id] = coachDef;
            else
                _package.HandCoachMoves[id] = coachDef;
        }

        // Ensure lyrics color metadata is up-to-date
        try
        {
            _package.Metadata.LyricsColor = LyricsDefinitionColor.ToString();
        }
        catch { }

        // Other clip sync: KaraokeClip and Pictogram durations are updated by their viewmodels on edit already, but be defensive and ensure durations are set
        foreach (TrackViewModel track in Tracks)
        {
            foreach (ClipViewModel clipVm in track.Clips)
            {
                if (clipVm.RawClip is KaraokeClip k)
                    k.Duration = (int)(clipVm.DurationBeats * 24);
                else if (clipVm.RawClip is PictogramClip p)
                    p.Duration = (int)(clipVm.DurationBeats * 24);
                // MoveClip has no duration field in the intermediate representation; move default durations are stored in coach move definitions
            }
        }

        // Finally, write package to disk
        IntermediatePackageSerializer.WriteToFolder(_package, RootPath);
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
        List<(TrackViewModel Track, ClipViewModel Clip)> toDelete = [];
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
        // Update the timeline-owned lyrics definition color (single source-of-truth)
        Color parsed = ClipViewModel.ParseRgbaHex(rgbaHexColor);
        Color normalized = new(255, parsed.R, parsed.G, parsed.B);
        if (!Equals(_lyricsDefinitionColor, normalized))
        {
            _lyricsDefinitionColor = normalized;
            _package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(_lyricsDefinitionColor);
            OnPropertyChanged(nameof(LyricsColor));
            OnPropertyChanged(nameof(LyricsDefinitionColor));
        }
    }

    private Color _lyricsDefinitionColor;
    public Color LyricsDefinitionColor
    {
        get => _lyricsDefinitionColor;
        set
        {
            Color normalized = new(255, value.R, value.G, value.B);
            if (!Equals(_lyricsDefinitionColor, normalized))
            {
                _lyricsDefinitionColor = normalized;
                _package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(_lyricsDefinitionColor);
                OnPropertyChanged(nameof(LyricsDefinitionColor));
                OnPropertyChanged(nameof(LyricsColor));
            }
        }
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