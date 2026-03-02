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
    /// <summary>Exposes the underlying song package for commands that need direct access.</summary>
    public IntermediateSongPackage Package { get; }

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

    /// <summary>Beat label where the audio file begins (= TimelineStructure.StartBeat).</summary>
    [ObservableProperty]
    public partial double AudioStartBeat { get; set; }

    /// <summary>Beat label where the audio file ends (derived from Playback.Duration).</summary>
    [ObservableProperty]
    public partial double AudioEndBeat { get; set; } = double.MaxValue;

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
    public TimelineStructureDocument TimelineStructure => Package.TimelineStructure;

    // Registry of known move definitions keyed by (id,isFullBody)
    private readonly Dictionary<(string id, bool isFullBody), MoveDefinitionViewModel> _moveDefinitions = [];
    public IReadOnlyDictionary<(string id, bool isFullBody), MoveDefinitionViewModel> MoveDefinitions => _moveDefinitions;

    public string AudioPath { get; private set; } = "";
    public string VideoPath { get; private set; } = "";
    public string PreparedAudioPath { get; private set; } = "";
    public double StartBeatValue { get; private set; }
    public double VideoOffset { get; private set; }
    public int CoachCount => Package.Metadata.CoachCount;
    public string LyricsColor => Package.Metadata.LyricsColor;

    public IEnumerable<string> AvailableHandCoachMoves => Package.HandCoachMoves.Keys;
    public IEnumerable<string> AvailableFullBodyCoachMoves => Package.FullBodyCoachMoves.Keys;

    /// <summary>
    /// Attempt to retrieve a color associated with the given move id from the package coach move definitions.
    /// Returns true if a color was found and parsed.
    /// </summary>
    public bool TryGetCoachMoveColor(string moveId, out Color color)
    {
        CoachMoveDefinition? def = null;
        if (Package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? d) || Package.FullBodyCoachMoves.TryGetValue(moveId, out d))
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
        Package = package;
        RootPath = rootPath;
        Id = package.Metadata.SongID.ToString();
        Title = package.Metadata.MapName;

        // Create undo service for this timeline
        UndoService = new UndoService();
        HookUndoServiceStateChanged();

        Playback = new PlaybackService();
        Playback.TimeChanged += (s, e) => CurrentBeat = Playback.CurrentBeat;

        BeatOffset = Package.TimelineStructure.StartBeat;
        MaxBeat = Package.TimelineStructure.EndBeat - Package.TimelineStructure.StartBeat;
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

        StartBeatValue = Package.TimelineStructure.StartBeat;
        VideoOffset = -Package.TimelineStructure.VideoStartOffset;

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
        TimelineStructureDocument ts = Package.TimelineStructure;
        double startOffset = ts.GetSongStartOffset();

        // Ensure this timeline is loaded
        await Playback.LoadMediaAsync(
            PreparedAudioPath,
            VideoPath,
            b => ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(b)),
            s => ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(s)),
            VideoOffset);

        // Initialize metronome timing so it's ready when enabled
        UpdateMetronomeTiming();

        // Record where audio starts/ends in beat-space for waveform clipping
        // (must be before SetExtendedEnd, as Duration will be overridden after that)
        AudioStartBeat = ts.StartBeat;
        double durSec = Playback.Duration.TotalSeconds;
        AudioEndBeat = durSec > 0 && ts.Markers.Count >= 2
            ? ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(durSec))
            : ts.EndBeat;

        // Extend playback with silence so the song plays through ts.EndBeat even if audio is short
        double endSec = ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(ts.EndBeat));
        Playback.SetExtendedEnd(TimeSpan.FromSeconds(endSec));

        WaveformSamples = await AudioConversionService.GetWaveformDataAsync(AudioPath);
    }

    private void BuildTimeline()
    {
        // Determine lyrics color once
        Color lyricsColor = Colors.Yellow;
        if (!string.IsNullOrEmpty(Package.Metadata.LyricsColor))
        {
            lyricsColor = ClipViewModel.ParseRgbaHex(Package.Metadata.LyricsColor);
        }

        // Store the lyrics definition color directly on the timeline as a Color field
        _lyricsDefinitionColor = new Color(255, lyricsColor.R, lyricsColor.G, lyricsColor.B);
        // Persist to metadata for consistency
        Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(_lyricsDefinitionColor);
        // Ensure UI knows lyrics color changed
        OnPropertyChanged(nameof(LyricsColor));
        OnPropertyChanged(nameof(LyricsDefinitionColor));

        // Ensure MoveDefinitions registry is populated from the package coach move definitions
        try
        {
            // Hand coach moves
            foreach (KeyValuePair<string, CoachMoveDefinition> kv in Package.HandCoachMoves)
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
            foreach (KeyValuePair<string, CoachMoveDefinition> kv in Package.FullBodyCoachMoves)
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
                    case HideUserInterfaceClip hic:
                        track.Clips.Add(new HideUserInterfaceClipViewModel(hic, hic.Duration, Colors.MediumPurple, string.Empty, RootPath, this));
                        break;
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
        // first a special track for the HUD hide events (below the audio waveform)
        AddTrack("Hide HUD", 30, Colors.MediumPurple, Package.HideUserInterface.Clips.Cast<TimelineClipBase>());

        AddTrack("Lyrics", 40, Colors.Goldenrod, Package.Lyrics.Clips.Cast<TimelineClipBase>());
        AddTrack("Pictograms", 60, Colors.CornflowerBlue, Package.Pictograms.Clips.Cast<TimelineClipBase>());

        // One track per coach timeline to preserve coach id in title
        foreach (MoveTimeline coachTimeline in Package.CoachTimelines)
            AddTrack($"Coach {coachTimeline.CoachId}", 40, Colors.MediumPurple, coachTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: false);

        foreach (MoveTimeline fullBodyTimeline in Package.FullBodyCoachTimelines)
            AddTrack($"FullBody Coach {fullBodyTimeline.CoachId}", 60, Colors.SeaGreen, fullBodyTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: true);

        AddTrack("Gold Effects", 30, Colors.OrangeRed, Package.GoldEffects.Clips.Cast<TimelineClipBase>());
    }

    /// <summary>
    /// Rebuilds the timeline in-place from the current (already-updated) package,
    /// and reloads playback with the new beat mapping. Audio is not re-converted.
    /// </summary>
    public async Task RebuildFromPackageAsync()
    {
        Playback.Pause();

        // Sync scalar properties from the updated package
        Title = Package.Metadata.MapName;
        BeatOffset = Package.TimelineStructure.StartBeat;
        MaxBeat = Package.TimelineStructure.EndBeat - Package.TimelineStructure.StartBeat;
        StartBeatValue = Package.TimelineStructure.StartBeat;
        VideoOffset = -Package.TimelineStructure.VideoStartOffset;
        UpdateTimelineWidth();

        // Notify the view that the structure document's sub-properties
        // (Sections, Signatures, Markers, etc.) have been replaced.
        OnPropertyChanged(nameof(StartBeatValue));
        OnPropertyChanged(nameof(VideoOffset));
        OnPropertyChanged(nameof(TimelineStructure));

        // Rebuild track list from the updated package
        Tracks.Clear();
        _moveDefinitions.Clear();
        BuildTimeline();

        // Reload playback with updated beat-to-time mapping
        // (reuse the already-converted WAV — no FFmpeg re-conversion needed)
        TimelineStructureDocument ts = Package.TimelineStructure;
        if (!string.IsNullOrEmpty(PreparedAudioPath) && File.Exists(PreparedAudioPath))
        {
            await Playback.LoadMediaAsync(
                PreparedAudioPath,
                VideoPath,
                b => ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(b)),
                s => ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(s)),
                VideoOffset);

            // Refresh metronome timing after edit
            UpdateMetronomeTiming();

            // Refresh audio beat bounds after re-load
            // (must be before SetExtendedEnd, as Duration will be overridden after that)
            AudioStartBeat = ts.StartBeat;
            double durSec = Playback.Duration.TotalSeconds;
            AudioEndBeat = durSec > 0 && ts.Markers.Count >= 2
                ? ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(durSec))
                : ts.EndBeat;

            // Extend playback with silence so the song plays through ts.EndBeat even if audio is short
            double endSec = ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(ts.EndBeat));
            Playback.SetExtendedEnd(TimeSpan.FromSeconds(endSec));

            WaveformSamples = await AudioConversionService.GetWaveformDataAsync(AudioPath);
        }
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
            if (Package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? d) || Package.FullBodyCoachMoves.TryGetValue(moveId, out d))
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
                Package.FullBodyCoachMoves[id] = coachDef;
            else
                Package.HandCoachMoves[id] = coachDef;
        }

        // Ensure lyrics color metadata is up-to-date
        try
        {
            Package.Metadata.LyricsColor = LyricsDefinitionColor.ToString();
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
                else if (clipVm.RawClip is HideUserInterfaceClip h)
                    h.Duration = (int)(clipVm.DurationBeats * 24);
                // MoveClip has no duration field in the intermediate representation; move default durations are stored in coach move definitions
            }
        }

        // Finally, write package to disk
        IntermediatePackageSerializer.WriteToFolder(Package, RootPath);
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

    public bool CanUndo => UndoService?.CanUndo ?? false;
    public bool CanRedo => UndoService?.CanRedo ?? false;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        UndoService.Undo();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        UndoService.Redo();
    }

    private void HookUndoServiceStateChanged()
    {
        UndoService?.StateChanged += (s, e) =>
            {
                // Notify bindings
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));

                // Notify generated commands to requery CanExecute
                try
                {
                    UndoCommand.NotifyCanExecuteChanged();
                }
                catch { }

                try
                {
                    RedoCommand.NotifyCanExecuteChanged();
                }
                catch { }
            };
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
            Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(_lyricsDefinitionColor);
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
                Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(_lyricsDefinitionColor);
                OnPropertyChanged(nameof(LyricsDefinitionColor));
                OnPropertyChanged(nameof(LyricsColor));
            }
        }
    }

    public string GetLyricsColor() => LyricsColor;

    // ─── Section / Signature editing ───

    /// <summary>
    /// Adds a section at the given beat with the given type.
    /// Records undo so the action can be reversed.
    /// </summary>
    public void AddSection(double beat, SongSectionType type)
    {
        // Prevent duplicate at same position
        if (TimelineStructure.Sections.Any(s => Math.Abs(s.StartBeat - beat) < 0.5))
            return;

        SectionSegment section = new() { SectionType = type, StartBeat = beat };
        TimelineStructure.Sections.Add(section);
        SortSections();

        PushUndo(
            undo: () =>
            {
                TimelineStructure.Sections.Remove(section);
                SortSections();
                NotifyStructureChanged();
            },
            redo: () =>
            {
                TimelineStructure.Sections.Add(section);
                SortSections();
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Removes a section from the timeline structure.
    /// </summary>
    public void RemoveSection(SectionSegment section)
    {
        if (!TimelineStructure.Sections.Contains(section))
            return;

        int index = TimelineStructure.Sections.IndexOf(section);
        SongSectionType type = section.SectionType;
        double beat = section.StartBeat;

        TimelineStructure.Sections.Remove(section);

        PushUndo(
            undo: () =>
            {
                TimelineStructure.Sections.Insert(Math.Min(index, TimelineStructure.Sections.Count), section);
                SortSections();
                NotifyStructureChanged();
            },
            redo: () =>
            {
                TimelineStructure.Sections.Remove(section);
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Moves a section to a new beat position (snapped to whole beat).
    /// </summary>
    public void MoveSection(SectionSegment section, double newBeat)
    {
        if (!TimelineStructure.Sections.Contains(section))
            return;

        // Prevent moving onto another section's position
        if (TimelineStructure.Sections.Any(s => s != section && Math.Abs(s.StartBeat - newBeat) < 0.5))
            return;

        double oldBeat = section.StartBeat;
        if (Math.Abs(oldBeat - newBeat) < 0.01)
            return;

        section.StartBeat = newBeat;
        SortSections();

        PushUndo(
            undo: () =>
            {
                section.StartBeat = oldBeat;
                SortSections();
                NotifyStructureChanged();
            },
            redo: () =>
            {
                section.StartBeat = newBeat;
                SortSections();
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Changes the type of an existing section.
    /// </summary>
    public void ChangeSectionType(SectionSegment section, SongSectionType newType)
    {
        SongSectionType oldType = section.SectionType;
        if (oldType == newType)
            return;

        section.SectionType = newType;

        PushUndo(
            undo: () =>
            {
                section.SectionType = oldType;
                NotifyStructureChanged();
            },
            redo: () =>
            {
                section.SectionType = newType;
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Adds a signature at the given beat with the given beats value.
    /// Records undo so the action can be reversed.
    /// </summary>
    public void AddSignature(double beat, int beats)
    {
        // Prevent duplicate at same position
        if (TimelineStructure.Signatures.Any(s => Math.Abs(s.Marker - beat) < 0.5))
            return;

        SignatureSegment sig = new() { Beats = beats, Marker = beat };
        TimelineStructure.Signatures.Add(sig);
        SortSignatures();

        PushUndo(
            undo: () =>
            {
                TimelineStructure.Signatures.Remove(sig);
                SortSignatures();
                NotifyStructureChanged();
            },
            redo: () =>
            {
                TimelineStructure.Signatures.Add(sig);
                SortSignatures();
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Removes a signature from the timeline structure.
    /// </summary>
    public void RemoveSignature(SignatureSegment sig)
    {
        if (!TimelineStructure.Signatures.Contains(sig))
            return;

        int index = TimelineStructure.Signatures.IndexOf(sig);
        TimelineStructure.Signatures.Remove(sig);

        PushUndo(
            undo: () =>
            {
                TimelineStructure.Signatures.Insert(Math.Min(index, TimelineStructure.Signatures.Count), sig);
                SortSignatures();
                NotifyStructureChanged();
            },
            redo: () =>
            {
                TimelineStructure.Signatures.Remove(sig);
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Moves a signature to a new beat position (snapped to whole beat).
    /// </summary>
    public void MoveSignature(SignatureSegment sig, double newBeat)
    {
        if (!TimelineStructure.Signatures.Contains(sig))
            return;

        // Prevent moving onto another signature's position
        if (TimelineStructure.Signatures.Any(s => s != sig && Math.Abs(s.Marker - newBeat) < 0.5))
            return;

        double oldBeat = sig.Marker;
        if (Math.Abs(oldBeat - newBeat) < 0.01)
            return;

        sig.Marker = newBeat;
        SortSignatures();

        PushUndo(
            undo: () =>
            {
                sig.Marker = oldBeat;
                SortSignatures();
                NotifyStructureChanged();
            },
            redo: () =>
            {
                sig.Marker = newBeat;
                SortSignatures();
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    /// <summary>
    /// Changes the beats value of an existing signature.
    /// </summary>
    public void ChangeSignatureBeats(SignatureSegment sig, int newBeats)
    {
        int oldBeats = sig.Beats;
        if (oldBeats == newBeats)
            return;

        sig.Beats = newBeats;

        PushUndo(
            undo: () =>
            {
                sig.Beats = oldBeats;
                NotifyStructureChanged();
            },
            redo: () =>
            {
                sig.Beats = newBeats;
                NotifyStructureChanged();
            });

        NotifyStructureChanged();
    }

    private void SortSections()
    {
        TimelineStructure.Sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
    }

    private void SortSignatures()
    {
        TimelineStructure.Signatures.Sort((a, b) => a.Marker.CompareTo(b.Marker));
    }

    /// <summary>
    /// Notifies the view that the structure (sections/signatures) has changed,
    /// causing timeline controls to re-render.
    /// </summary>
    public void NotifyStructureChanged()
    {
        OnPropertyChanged(nameof(TimelineStructure));

        // Keep metronome in sync with section/signature changes
        if (IsMetronomeEnabled)
            UpdateMetronomeTiming();
    }

    // ─── Metronome ───

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetronomeIcon))]
    public partial bool IsMetronomeEnabled { get; set; }

    public string MetronomeIcon => IsMetronomeEnabled ? "🔔" : "🔕";

    partial void OnIsMetronomeEnabledChanged(bool value)
    {
        Playback.IsMetronomeEnabled = value;
        if (value)
            UpdateMetronomeTiming();
    }

    /// <summary>
    /// Updates the metronome timing from the current timeline structure.
    /// Call this when sections/signatures change while metronome is enabled.
    /// </summary>
    private void UpdateMetronomeTiming()
    {
        TimelineStructureDocument ts = Package.TimelineStructure;

        // Derive zeroBeatTime, bpm, beatsPerMeasure from timeline markers
        double zeroBeatTime = 0;
        double bpm = 120;
        int beatsPerMeasure = 4;

        if (ts.Markers.Count >= 2)
        {
            double beatDurationSec = (ts.Markers[1] - ts.Markers[0]) / 48000.0;
            if (beatDurationSec > 0)
                bpm = 60.0 / beatDurationSec;

            int zeroBeatIndex = -ts.StartBeat;
            if (zeroBeatIndex >= 0 && zeroBeatIndex < ts.Markers.Count)
                zeroBeatTime = ts.Markers[zeroBeatIndex] / 48000.0;
        }

        if (ts.Signatures.Count > 0)
            beatsPerMeasure = ts.Signatures[0].Beats;

        // Convert SectionSegments to beat positions for the metronome
        Playback.UpdateMetronome(zeroBeatTime, bpm, beatsPerMeasure, ts.Sections.Select(s => (double)s.StartBeat));
    }

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