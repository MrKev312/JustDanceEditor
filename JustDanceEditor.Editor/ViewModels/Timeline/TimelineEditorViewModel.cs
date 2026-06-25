// File: .\ViewModels\Timeline\TimelineEditorViewModel.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Formats.JDI.Video;

using SixLabors.ImageSharp.Processing;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class TimelineEditorViewModel : Document
{
    private static readonly string[] SupportedVideoExtensions = ["*.webm", "*.mp4", "*.mkv", "*.mov"];

    private readonly List<ClipClipboardEntry> _clipClipboard = [];

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

    /// <summary>True while the initial audio conversion and media load is in progress.</summary>
    [ObservableProperty]
    public partial bool IsMediaLoading { get; set; } = false;

    /// <summary>Non-null when <see cref="InitializeMedia"/> failed; contains a human-readable message.</summary>
    [ObservableProperty]
    public partial string? MediaLoadError { get; set; }

    // Static synchronization for snapping across all timeline instances is now handled by TimelineSettingsService.

    // suppress broadcasting when applying remote changes
    private bool _suppressSnapBroadcast = false;
    private readonly TimelineSettingsService _settings;
    private ITimelinePictogramGenerator? _pictogramGenerator;
    private string _baseTitle = string.Empty;

    /// <summary>Set to true by <see cref="PromptSaveOnCloseAsync"/> to allow the
    /// second <see cref="OnClose"/> call to proceed after the user confirms.</summary>
    private bool _allowClose = false;

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];
    public IPlaybackService Playback { get; }
    public TimelineStructureDocument TimelineStructure => Package.TimelineStructure;

    // Registry of known move definitions keyed by (id,isFullBody)
    private readonly Dictionary<(string id, bool isFullBody), MoveDefinitionViewModel> _moveDefinitions = [];
    public IReadOnlyDictionary<(string id, bool isFullBody), MoveDefinitionViewModel> MoveDefinitions => _moveDefinitions;

    public string AudioPath { get; private set; } = "";
    public string VideoPath { get; private set; } = "";
    public PcmWaveAudioData? PreparedAudio { get; private set; }
    public double StartBeatValue { get; private set; }
    public double VideoOffset { get; private set; }
    public double VideoDurationSeconds { get; private set; }
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

        if (def != null)
        {
            color = ClipViewModel.ParseRgbaHex(def.Color);
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
                 .OfType<string>()
                 .OrderBy(x => x);
        }
    }

    // Simple undo/redo stack
    private readonly Stack<(Action Undo, Action Redo)> _undoStack = new();
    private readonly Stack<(Action Undo, Action Redo)> _redoStack = new();

    public TimelineEditorViewModel(
        IntermediateSongPackage package,
        string rootPath,
        IPlaybackService playback,
        TimelineSettingsService settings,
        ITimelinePictogramGenerator? pictogramGenerator = null)
    {
        Package = package;
        RootPath = rootPath;
        Id = package.Metadata.SongID.ToString();
        _baseTitle = package.Metadata.MapName;
        Title = _baseTitle;
        _pictogramGenerator = pictogramGenerator;

        // Create undo service for this timeline
        UndoService = new UndoService();
        HookUndoServiceStateChanged();

        Playback = playback;
        Playback.TimeChanged += (s, e) => CurrentBeat = Playback.CurrentBeat;

        BeatOffset = Package.TimelineStructure.StartBeat;
        MaxBeat = Package.TimelineStructure.EndBeat - Package.TimelineStructure.StartBeat;
        UpdateTimelineWidth();

        BuildTimeline();

        // Bind to the shared snap settings service
        _settings = settings;
        _suppressSnapBroadcast = true;
        SnapToGrid = settings.SnapToGrid;
        SnapToCurrentTimeMarker = settings.SnapToCurrentTimeMarker;
        SnapGridSize = settings.SnapGridSize;
        SnapThreshold = settings.SnapThreshold;
        SnapToClips = settings.SnapToClips;
        _suppressSnapBroadcast = false;

        // Keep in sync when another timeline changes the settings
        settings.PropertyChanged += (s, e) =>
        {
            _suppressSnapBroadcast = true;
            switch (e.PropertyName)
            {
                case nameof(TimelineSettingsService.SnapToGrid) when SnapToGrid != settings.SnapToGrid:
                    SnapToGrid = settings.SnapToGrid; break;
                case nameof(TimelineSettingsService.SnapToCurrentTimeMarker) when SnapToCurrentTimeMarker != settings.SnapToCurrentTimeMarker:
                    SnapToCurrentTimeMarker = settings.SnapToCurrentTimeMarker; break;
                case nameof(TimelineSettingsService.SnapGridSize) when Math.Abs(SnapGridSize - settings.SnapGridSize) > 1e-9:
                    SnapGridSize = settings.SnapGridSize; break;
                case nameof(TimelineSettingsService.SnapThreshold) when Math.Abs(SnapThreshold - settings.SnapThreshold) > 1e-9:
                    SnapThreshold = settings.SnapThreshold; break;
                case nameof(TimelineSettingsService.SnapToClips) when SnapToClips != settings.SnapToClips:
                    SnapToClips = settings.SnapToClips; break;
            }

            _suppressSnapBroadcast = false;
        };
    }

    /// <summary>
    /// Performs the async media initialisation (audio conversion, video discovery, playback load).
    /// Must be called after construction — not invoked from the constructor to avoid fire-and-forget.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsMediaLoading = true;
        MediaLoadError = null;

        try
        {
            AudioPath = Path.Combine(RootPath, IntermediatePackageLayout.Assets.AudioMasterFile);
            string videoDir = Path.Combine(RootPath, IntermediatePackageLayout.Assets.VideoFolder);
            VideoPath = "";
            VideoDurationSeconds = 0;

            if (Directory.Exists(videoDir))
            {
                FileInfo? selectedVideo = SupportedVideoExtensions
                    .SelectMany(pattern => Directory.GetFiles(videoDir, pattern))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(fi => fi.Length)
                    .FirstOrDefault();

                if (selectedVideo != null)
                {
                    VideoPath = selectedVideo.FullName;
                    VideoDurationSeconds = await GetMediaDurationAsync(VideoPath);
                }
            }

            OnPropertyChanged(nameof(VideoPath));
            OnPropertyChanged(nameof(VideoDurationSeconds));

            StartBeatValue = Package.TimelineStructure.StartBeat;
            SetVideoOffset(-Package.TimelineStructure.VideoStartOffset, syncTrack: false);
            SyncVideoTrackClip();

            // Prepare audio once so playback can reuse decoded PCM without temp files.
            if (File.Exists(AudioPath))
            {
                PreparedAudio = await AudioConversionService.DecodeToPcmAsync(AudioPath);
            }

            // Marker-based timing logic
            TimelineStructureDocument ts = Package.TimelineStructure;
            double startOffset = ts.GetSongStartOffset();

            // Ensure this timeline is loaded
            await Playback.LoadMediaAsync(
                PreparedAudio,
                b => ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(b)),
                s => ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(s)));

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
        catch (Exception ex)
        {
            MediaLoadError = $"Failed to load media: {ex.Message}";
        }
        finally
        {
            IsMediaLoading = false;
        }
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
                    Color = ClipViewModel.ParseRgbaHex(def.Color)
                };
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
                    Color = ClipViewModel.ParseRgbaHex(def.Color)
                };
                _moveDefinitions[(id, true)] = md;
            }
        }
        catch { }

        // Helper-local to capture lyricsColor where needed
        void AddTrack(string title, double height, Color color, TrackType trackType, IEnumerable<TimelineClipBase> clips, bool isFullBody = false)
        {
            TrackViewModel track = new() { Title = title, Height = height, TrackColor = color, TrackType = trackType };
            foreach (TimelineClipBase clip in clips)
            {
                switch (clip)
                {
                    case HideUserInterfaceClip hic:
                        track.Clips.Add(new HideUserInterfaceClipViewModel(hic, RootPath, this));
                        break;
                    case KaraokeClip kc:
                        track.Clips.Add(new KaraokeClipViewModel(kc, RootPath, this));
                        break;
                    case PictogramClip pc:
                        track.Clips.Add(new PictogramClipViewModel(pc, RootPath, this));
                        break;
                    case MoveClip mc:
                        {
                            MoveDefinitionViewModel defVm = GetOrRegisterMove(mc.MoveId, isFullBody);
                            track.Clips.Add(new MoveClipViewModel(mc, RootPath, this, isFullBody, (int)defVm.DefaultDuration));
                            break;
                        }
                    case GoldEffectClip gc:
                        track.Clips.Add(new GoldEffectClipViewModel(gc, RootPath, this));
                        break;
                    default:
                        // Unknown clip type - fallback to GoldEffect wrapper
                        track.Clips.Add(new GoldEffectClipViewModel(new GoldEffectClip(), RootPath, this));
                        break;
                }
            }

            Tracks.Add(track);
        }

        // Build tracks using configuration-style calls (keeps BuildTimeline concise)
        AddTrack("Video", 40, Colors.IndianRed, TrackType.Video, []);

        // first a special track for the HUD hide events (below the audio waveform)
        AddTrack("Hide HUD", 30, Colors.MediumPurple, TrackType.HideHud, Package.HideUserInterface.Clips.Cast<TimelineClipBase>());

        AddTrack("Lyrics", 40, Colors.Goldenrod, TrackType.Lyrics, Package.Lyrics.Clips.Cast<TimelineClipBase>());
        AddTrack("Pictograms", 60, Colors.CornflowerBlue, TrackType.Pictogram, Package.Pictograms.Clips.Cast<TimelineClipBase>());

        // One track per coach timeline to preserve coach id in title
        foreach (MoveTimeline coachTimeline in Package.CoachTimelines)
            AddTrack($"Coach {coachTimeline.CoachId}", 40, Colors.MediumPurple, TrackType.CoachHand, coachTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: false);

        foreach (MoveTimeline fullBodyTimeline in Package.FullBodyCoachTimelines)
            AddTrack($"FullBody Coach {fullBodyTimeline.CoachId}", 60, Colors.SeaGreen, TrackType.CoachFullBody, fullBodyTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: true);

        AddTrack("Gold Effects", 30, Colors.OrangeRed, TrackType.GoldEffect, Package.GoldEffects.Clips.Cast<TimelineClipBase>());

        SyncVideoTrackClip();
    }

    /// <summary>
    /// Rebuilds the timeline in-place from the current (already-updated) package,
    /// and reloads playback with the new beat mapping. Audio is not re-converted.
    /// </summary>
    public async Task RebuildFromPackageAsync()
    {
        Playback.Pause();

        // Sync scalar properties from the updated package
        _baseTitle = Package.Metadata.MapName;
        Title = UndoService.IsDirty ? $"{_baseTitle} *" : _baseTitle;
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
        SyncVideoTrackClip();

        // Reload playback with updated beat-to-time mapping.
        TimelineStructureDocument ts = Package.TimelineStructure;
        if (PreparedAudio != null)
        {
            await Playback.LoadMediaAsync(
                PreparedAudio,
                b => ts.GetSecondsAtBeat(ts.GetIndexFromBeatLabel(b)),
                s => ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(s)));

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

    public double GetPlaybackSecondsAtBeatLabel(double beatLabel)
    {
        if (TimelineStructure.Markers.Count < 2)
            return TimelineStructure.GetIndexFromBeatLabel(beatLabel);

        return TimelineStructure.GetSecondsAtBeat(TimelineStructure.GetIndexFromBeatLabel(beatLabel));
    }

    public double GetBeatLabelAtPlaybackSeconds(double seconds)
    {
        if (TimelineStructure.Markers.Count < 2)
            return TimelineStructure.GetBeatLabelFromIndex(seconds);

        return TimelineStructure.GetBeatLabelFromIndex(TimelineStructure.GetBeatAtSeconds(seconds));
    }

    public void SetVideoOffsetFromClipStartBeat(double startBeat)
    {
        double playbackStartSeconds = GetPlaybackSecondsAtBeatLabel(startBeat);
        double songStartOffset = TimelineStructure.GetSongStartOffset();
        SetVideoOffset(-(playbackStartSeconds + songStartOffset));
    }

    public void SetVideoOffset(double offsetSeconds)
    {
        SetVideoOffset(offsetSeconds, syncTrack: true);
    }

    private void SetVideoOffset(double offsetSeconds, bool syncTrack)
    {
        if (Math.Abs(VideoOffset - offsetSeconds) <= 1e-9 && Math.Abs(Package.TimelineStructure.VideoStartOffset + offsetSeconds) <= 1e-9)
            return;

        Package.TimelineStructure.VideoStartOffset = -offsetSeconds;
        VideoOffset = offsetSeconds;
        OnPropertyChanged(nameof(VideoOffset));

        if (syncTrack)
            SyncVideoTrackClip();
    }

    private void SyncVideoTrackClip()
    {
        TrackViewModel? videoTrack = Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
        if (videoTrack == null)
            return;

        if (string.IsNullOrWhiteSpace(VideoPath) || VideoDurationSeconds <= 0)
        {
            videoTrack.Clips.Clear();
            return;
        }

        VideoClipViewModel? videoClip = videoTrack.Clips.OfType<VideoClipViewModel>().FirstOrDefault();
        if (videoClip == null)
        {
            videoTrack.Clips.Clear();
            videoTrack.Clips.Add(new VideoClipViewModel(VideoDurationSeconds, RootPath, this));
            return;
        }

        videoClip.RefreshFromTimeline();
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

    public MoveDefinitionViewModel GetOrRegisterMove(string moveId, bool isFullBody)
    {
        if (string.IsNullOrEmpty(moveId))
            return new MoveDefinitionViewModel { Id = moveId, IsFullBody = isFullBody };

        (string moveId, bool isFullBody) key = (moveId, isFullBody);
        if (_moveDefinitions.TryGetValue(key, out MoveDefinitionViewModel? def))
        {
            // update asset presence in case files were added/removed since first registration
            def.HasAsset = CheckMoveFileExists(moveId, isFullBody);
            return def;
        }

        // Attempt to seed from package if possible
        Color color = Colors.LightGray;
        double duration = 24.0;
        try
        {
            if (Package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? d) || Package.FullBodyCoachMoves.TryGetValue(moveId, out d))
            {
                if (d != null)
                {
                    color = ClipViewModel.ParseRgbaHex(d.Color);
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
            DefaultDuration = duration,
            HasAsset = CheckMoveFileExists(moveId, isFullBody)
        };

        _moveDefinitions[key] = def;
        return def;
    }

    /// <summary>
    /// Determines whether the MSM/gesture file for <paramref name="moveId"/> exists in
    /// the current root path. Uses <see cref="IntermediatePackageLayout"/> to resolve
    /// the expected folder, and treats missing <see cref="RootPath"/> gracefully.
    /// </summary>
    private bool CheckMoveFileExists(string moveId, bool isFullBody)
    {
        try
        {
            if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(moveId))
                return false;

            string folderRel = isFullBody
                ? IntermediatePackageLayout.Assets.GesturesFolder
                : IntermediatePackageLayout.Assets.MovesFolder;

            string folderAbs = IntermediatePackageLayout.Resolve(RootPath, folderRel);
            string ext = isFullBody ? ".gesture" : ".msm";
            string candidate = Path.Combine(folderAbs, moveId + ext);
            return File.Exists(candidate);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers a brand-new move definition in the package (and the in-memory cache)
    /// so it becomes available for placement on the timeline.
    /// Records the action on the undo/redo stack.
    /// </summary>
    /// <returns>
    /// The newly created <see cref="MoveDefinitionViewModel"/>, or <c>null</c> if the
    /// id is empty or already registered.
    /// </returns>
    public MoveDefinitionViewModel? RegisterNewMoveDefinition(string id, bool isFullBody, int durationFrames, Color color)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        Dictionary<string, CoachMoveDefinition> catalog = isFullBody
            ? Package.FullBodyCoachMoves
            : Package.HandCoachMoves;

        // Don't allow duplicates
        if (catalog.ContainsKey(id))
            return GetOrRegisterMove(id, isFullBody);

        string colorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        CoachMoveDefinition pkgDef = new()
        {
            Color = colorHex,
            Duration = durationFrames,
            MoveType = isFullBody ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking
        };

        MoveDefinitionViewModel vm = new()
        {
            Id = id,
            IsFullBody = isFullBody,
            Color = color,
            DefaultDuration = durationFrames,
            HasAsset = CheckMoveFileExists(id, isFullBody)
        };

        // Record undo/redo to the stack
        PushUndo(
            undo: () =>
            {
                catalog.Remove(id);
                _moveDefinitions.Remove((id, isFullBody));
                OnPropertyChanged(isFullBody
                    ? nameof(AvailableFullBodyCoachMoves)
                    : nameof(AvailableHandCoachMoves));
            },
            redo: () =>
            {
                catalog[id] = pkgDef;
                _moveDefinitions[(id, isFullBody)] = vm;
                OnPropertyChanged(isFullBody
                    ? nameof(AvailableFullBodyCoachMoves)
                    : nameof(AvailableHandCoachMoves));
            }
        );

        // Perform the action
        catalog[id] = pkgDef;
        _moveDefinitions[(id, isFullBody)] = vm;

        // Notify consumers that the available-moves list has changed
        OnPropertyChanged(isFullBody
            ? nameof(AvailableFullBodyCoachMoves)
            : nameof(AvailableHandCoachMoves));

        return vm;
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
                Color = ClipViewModel.ColorToRgbHex(def.Color),
                Duration = (int)def.DefaultDuration,
                MoveType = isFull ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking
            };

            if (isFull)
                Package.FullBodyCoachMoves[id] = coachDef;
            else
                Package.HandCoachMoves[id] = coachDef;
        }

        // Sync track clip lists back to their package collections (handles add/delete operations)
        foreach (TrackViewModel track in Tracks)
        {
            switch (track.TrackType)
            {
                case TrackType.Lyrics:
                    Package.Lyrics.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is KaraokeClip kc)
                            Package.Lyrics.Clips.Add(kc);
                    break;
                case TrackType.Pictogram:
                    Package.Pictograms.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is PictogramClip pc)
                            Package.Pictograms.Clips.Add(pc);
                    break;
                case TrackType.HideHud:
                    Package.HideUserInterface.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is HideUserInterfaceClip hic)
                            Package.HideUserInterface.Clips.Add(hic);
                    break;
                case TrackType.GoldEffect:
                    Package.GoldEffects.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is GoldEffectClip gc)
                            Package.GoldEffects.Clips.Add(gc);
                    break;
                case TrackType.CoachHand:
                    {
                        string idStr = track.Title.StartsWith("Coach ") ? track.Title["Coach ".Length..] : string.Empty;
                        if (int.TryParse(idStr, out int coachId))
                        {
                            MoveTimeline? mt = Package.CoachTimelines.FirstOrDefault(t => t.CoachId == coachId);
                            if (mt != null)
                            {
                                mt.Clips.Clear();
                                foreach (ClipViewModel c in track.Clips)
                                    if (c.RawClip is MoveClip mc)
                                        mt.Clips.Add(mc);
                            }
                        }

                        break;
                    }
                case TrackType.CoachFullBody:
                    {
                        string idStr = track.Title.StartsWith("FullBody Coach ") ? track.Title["FullBody Coach ".Length..] : string.Empty;
                        if (int.TryParse(idStr, out int coachId))
                        {
                            MoveTimeline? mt = Package.FullBodyCoachTimelines.FirstOrDefault(t => t.CoachId == coachId);
                            if (mt != null)
                            {
                                mt.Clips.Clear();
                                foreach (ClipViewModel c in track.Clips)
                                    if (c.RawClip is MoveClip mc)
                                        mt.Clips.Add(mc);
                            }
                        }

                        break;
                    }
            }
        }

        // Ensure lyrics color metadata is up-to-date (must use our RGBA hex helper;
        // Color.ToString() returns ARGB which confuses the loader).
        Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(LyricsDefinitionColor);

        // Finally, write package to disk
        IntermediatePackageSerializer.WriteToFolder(Package, RootPath);

        // Mark this stack position as saved so the dirty indicator clears
        UndoService.MarkSaved();
        Title = _baseTitle;
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

    public async Task GeneratePictogramsAsync(PictogramGenerationMode mode, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default)
    {
        ITimelinePictogramGenerator generator = _pictogramGenerator ??= new TimelinePictogramGenerator();

        GeneratedPictogramBatch batch;
        try
        {
            batch = await generator.GenerateAsync(this, mode, frameLayoutMode, horizontalFocus, cancellationToken);
        }
        catch
        {
            return;
        }

        if (batch.PictogramTrack == null || batch.Clips.Count == 0)
            return;

        TrackViewModel targetTrack = batch.PictogramTrack;
        IReadOnlyList<PictogramClipViewModel> generatedClips = batch.Clips;

        PushUndo(
            undo: () =>
            {
                foreach (PictogramClipViewModel clip in generatedClips)
                {
                    if (targetTrack.Clips.Contains(clip))
                        targetTrack.Clips.Remove(clip);
                }
            },
            redo: () =>
            {
                foreach (PictogramClipViewModel clip in generatedClips)
                {
                    if (!targetTrack.Clips.Contains(clip))
                        targetTrack.Clips.Add(clip);
                }
            }
        );

        foreach (PictogramClipViewModel clip in generatedClips)
        {
            if (!targetTrack.Clips.Contains(clip))
                targetTrack.Clips.Add(clip);
        }

        // Keep timeline tools (including Library) in sync with both generated files and new clip references.
        OnPropertyChanged(nameof(AvailablePictograms));
        OnPropertyChanged(nameof(Tracks));
    }

    public async Task GenerateNewPictogramAsync(double playheadBeat, PictogramScreenshotOptionsResult options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        TrackViewModel? pictogramTrack = Tracks.FirstOrDefault(t => t.TrackType == TrackType.Pictogram);
        if (pictogramTrack == null)
            return;

        if (string.IsNullOrWhiteSpace(VideoPath) || !File.Exists(VideoPath))
            return;

        string pictogramDirectory = Path.Combine(RootPath, "assets", "pictograms");
        Directory.CreateDirectory(pictogramDirectory);

        string baseId = BuildNewPictogramBaseId(options);
        string pictogramId = EnsureUniquePictogramId(baseId, new HashSet<string>(AvailablePictograms, StringComparer.OrdinalIgnoreCase));

        string outputPath = Path.Combine(pictogramDirectory, pictogramId + ".webp");
        PixelSize targetSize = TimelinePictogramGenerator.GetTargetSize(CoachCount);
        double videoTimestamp = GetVideoTimestampSecondsFromBeat(playheadBeat);

        PictogramImageGenerator imageGenerator = new();
        await imageGenerator.GenerateVideoFramePictogramAsync(
            VideoPath,
            videoTimestamp,
            outputPath,
            targetSize,
            options.FrameLayoutMode,
            options.HorizontalFocus,
            cancellationToken);

        ImageBitmapCache.Invalidate(outputPath);

        IEnumerable<MoveClipViewModel> moveClips = Tracks
            .Where(t => t.TrackType is TrackType.CoachHand or TrackType.CoachFullBody)
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>();

        List<double> insertionBeats = BuildPictogramInsertionBeats(moveClips, playheadBeat, options);

        List<PictogramClipViewModel> created = [];
        foreach (double beat in insertionBeats)
        {
            double clampedBeat = ClampBeatForDuration(beat, 24);
            PictogramClip raw = new()
            {
                PictogramId = pictogramId,
                StartTime = (int)Math.Round(clampedBeat * 24d),
                Duration = 24
            };

            created.Add(new PictogramClipViewModel(raw, RootPath, this));
        }

        if (created.Count == 0)
            return;

        PushUndo(
            undo: () =>
            {
                foreach (PictogramClipViewModel clip in created)
                {
                    if (pictogramTrack.Clips.Contains(clip))
                        pictogramTrack.Clips.Remove(clip);
                }
            },
            redo: () =>
            {
                foreach (PictogramClipViewModel clip in created)
                {
                    if (!pictogramTrack.Clips.Contains(clip))
                        pictogramTrack.Clips.Add(clip);
                }
            }
        );

        foreach (PictogramClipViewModel clip in created)
        {
            if (!pictogramTrack.Clips.Contains(clip))
                pictogramTrack.Clips.Add(clip);
        }

        OnPropertyChanged(nameof(AvailablePictograms));
        OnPropertyChanged(nameof(Tracks));
    }

    public static List<double> BuildPictogramInsertionBeats(IEnumerable<MoveClipViewModel> moveClips, double playheadBeat, PictogramScreenshotOptionsResult options)
    {
        ArgumentNullException.ThrowIfNull(moveClips);
        ArgumentNullException.ThrowIfNull(options);

        if (options.InsertionMode != PictogramInsertionMode.AllInstancesOfSelectedMove
            || string.IsNullOrWhiteSpace(options.ReferenceMoveId)
            || !options.ReferenceMoveStartFrame.HasValue)
        {
            return [playheadBeat];
        }

        double offset = playheadBeat - (options.ReferenceMoveStartFrame.Value / 24d);

        List<double> beats = [.. moveClips
            .Where(c => string.Equals(c.MoveId, options.ReferenceMoveId, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.RawClip.StartTime)
            .Distinct()
            .OrderBy(startFrame => startFrame)
            .Select(startFrame => (startFrame / 24d) + offset)];

        return beats.Count > 0 ? beats : [playheadBeat];
    }

    public static string BuildNewPictogramBaseId(PictogramScreenshotOptionsResult options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string seed = string.IsNullOrWhiteSpace(options.ReferenceMoveId)
            ? "screenshot"
            : options.ReferenceMoveId;

        return $"auto_{TimelinePictogramGenerator.SanitizeId(seed)}";
    }

    public bool RenamePictogramId(string oldId, string newId)
    {
        oldId = oldId?.Trim() ?? string.Empty;
        newId = newId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
            return false;

        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            return false;

        HashSet<string> usedIds = new(AvailablePictograms, StringComparer.OrdinalIgnoreCase);
        if (usedIds.Contains(newId))
            return false;

        string oldPath = ResolvePictogramPath(oldId);
        if (!File.Exists(oldPath))
            return false;

        string extension = Path.GetExtension(oldPath);
        string directory = Path.GetDirectoryName(oldPath) ?? Path.Combine(RootPath, "assets", "pictograms");
        string newPath = Path.Combine(directory, newId + extension);
        if (File.Exists(newPath))
            return false;

        List<PictogramClipViewModel> affected = [.. Tracks
            .SelectMany(t => t.Clips)
            .OfType<PictogramClipViewModel>()
            .Where(c => string.Equals(c.PictogramId, oldId, StringComparison.OrdinalIgnoreCase))];

        void ApplyRename(string fromId, string toId, string fromPath, string toPath)
        {
            if (File.Exists(fromPath) && !File.Exists(toPath))
                File.Move(fromPath, toPath);

            foreach (PictogramClipViewModel clip in affected)
                clip.PictogramId = toId;

            ImageBitmapCache.Invalidate(fromPath);
            ImageBitmapCache.Invalidate(toPath);

            OnPropertyChanged(nameof(AvailablePictograms));
            OnPropertyChanged(nameof(Tracks));
        }

        ApplyRename(oldId, newId, oldPath, newPath);

        PushUndo(
            undo: () => ApplyRename(newId, oldId, newPath, oldPath),
            redo: () => ApplyRename(oldId, newId, oldPath, newPath)
        );

        return true;
    }

    /// <summary>
    /// Flip a pictogram asset: toggles between <id> and <id>_flipped.
    /// If the counterpart exists, simply relinks clips (either a specific clip or all instances).
    /// Otherwise creates a flipped copy (lossless for webp) and relinks.
    /// Returns true when operation completed successfully.
    /// </summary>
    public async Task<bool> FlipPictogramAsync(string pictogramId, PictogramClipViewModel? specificClip = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pictogramId))
            return false;

        string pictogramDirectory = Path.Combine(RootPath, "assets", "pictograms");
        Directory.CreateDirectory(pictogramDirectory);

        string? currentPath = GetExistingPictogramFilePath(pictogramId);
        if (currentPath == null || !File.Exists(currentPath))
            return false;

        bool isFlipped = pictogramId.EndsWith("_flipped", StringComparison.OrdinalIgnoreCase);
        string counterpartId = isFlipped ? pictogramId[..^"_flipped".Length] : pictogramId + "_flipped";

        string? counterpartPath = GetExistingPictogramFilePath(counterpartId);

        List<PictogramClipViewModel> affectedClips;
        if (specificClip != null)
            affectedClips = [specificClip];
        else
            affectedClips = [.. Tracks.SelectMany(t => t.Clips).OfType<PictogramClipViewModel>().Where(c => string.Equals(c.PictogramId, pictogramId, StringComparison.OrdinalIgnoreCase))];

        // If counterpart already exists, just relink
        if (!string.IsNullOrWhiteSpace(counterpartPath) && File.Exists(counterpartPath))
        {
            void ApplyRelink(string fromId, string toId)
            {
                foreach (PictogramClipViewModel c in affectedClips)
                    c.PictogramId = toId;

                ImageBitmapCache.Invalidate(currentPath);
                ImageBitmapCache.Invalidate(counterpartPath);
                OnPropertyChanged(nameof(AvailablePictograms));
                OnPropertyChanged(nameof(Tracks));
            }

            ApplyRelink(pictogramId, Path.GetFileNameWithoutExtension(counterpartPath));

            PushUndo(
                undo: () => ApplyRelink(Path.GetFileNameWithoutExtension(counterpartPath), pictogramId),
                redo: () => ApplyRelink(pictogramId, Path.GetFileNameWithoutExtension(counterpartPath))
            );

            return true;
        }

        // Need to create counterpart by flipping currentPath -> counterpartPath (preserve extension)
        string ext = Path.GetExtension(currentPath);
        string destPath = Path.Combine(pictogramDirectory, counterpartId + ext);
        if (File.Exists(destPath))
            counterpartPath = destPath; // race fallback

        bool created = false;
        try
        {
            if (!File.Exists(destPath))
            {
                // Use ImageSharp to perform a lossless horizontal flip when possible.
                try
                {
                    using SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Bgra32> image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(currentPath);
                    image.Mutate(x => x.Flip(FlipMode.Horizontal));

                    // Preserve webp lossless encoding when target is webp and encoder is available
                    if (string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase))
                    {
                        using FileStream outFs = File.Create(destPath);
                        image.Save(outFs, WebpSettings.LosslessWebpEncoder);
                    }
                    else
                    {
                        // For other formats, choose an encoder matching the extension
                        using FileStream outFs = File.Create(destPath);
                        if (string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase))
                        {
                            image.Save(outFs, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                        }
                        else if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
                        {
                            image.Save(outFs, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder() { Quality = 90 });
                        }
                        else
                        {
                            // Fallback to PNG encoder
                            image.Save(outFs, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                        }
                    }

                    created = File.Exists(destPath);
                }
                catch
                {
                    created = false;
                }
            }

            if (!File.Exists(destPath))
                return false;

            counterpartPath = destPath;

            // Apply relink to affected clips
            string toId = Path.GetFileNameWithoutExtension(counterpartPath);

            void ApplyRelinkCreated(string fromId, string toIdLocal)
            {
                foreach (PictogramClipViewModel c in affectedClips)
                    c.PictogramId = toIdLocal;

                ImageBitmapCache.Invalidate(currentPath);
                ImageBitmapCache.Invalidate(counterpartPath);
                OnPropertyChanged(nameof(AvailablePictograms));
                OnPropertyChanged(nameof(Tracks));
            }

            ApplyRelinkCreated(pictogramId, toId);

            PushUndo(
                undo: () =>
                {
                    // revert clip ids
                    foreach (PictogramClipViewModel c in affectedClips)
                        c.PictogramId = pictogramId;

                    // attempt to delete created file
                    try
                    {
                        if (File.Exists(destPath))
                            File.Delete(destPath);
                    }
                    catch { }

                    ImageBitmapCache.Invalidate(destPath);
                    ImageBitmapCache.Invalidate(currentPath);
                    OnPropertyChanged(nameof(AvailablePictograms));
                    OnPropertyChanged(nameof(Tracks));
                },
                redo: () => ApplyRelinkCreated(pictogramId, toId)
            );

            return true;
        }
        catch
        {
            if (created && File.Exists(destPath))
            {
                try
                {
                    File.Delete(destPath);
                }
                catch { }
            }

            return false;
        }
    }

    private string? GetExistingPictogramFilePath(string pictogramId)
    {
        if (string.IsNullOrWhiteSpace(pictogramId))
            return null;

        string directory = Path.Combine(RootPath, "assets", "pictograms");
        string[] preferred = [".webp", ".png", ".jpg", ".jpeg"];

        foreach (string ext in preferred)
        {
            string candidate = Path.Combine(directory, pictogramId + ext);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public bool RenameMoveId(string oldId, string newId, bool isFullBody)
    {
        oldId = oldId?.Trim() ?? string.Empty;
        newId = newId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
            return false;

        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            return false;

        Dictionary<string, CoachMoveDefinition> catalog = isFullBody ? Package.FullBodyCoachMoves : Package.HandCoachMoves;
        if (!catalog.TryGetValue(oldId, out CoachMoveDefinition? definition) || definition == null)
            return false;

        if (catalog.ContainsKey(newId))
            return false;

        TrackType targetTrackType = isFullBody ? TrackType.CoachFullBody : TrackType.CoachHand;
        List<MoveClipViewModel> affected = [.. Tracks
            .Where(t => t.TrackType == targetTrackType)
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()
            .Where(c => string.Equals(c.MoveId, oldId, StringComparison.OrdinalIgnoreCase))];

        void ApplyRename(string fromId, string toId)
        {
            catalog.Remove(fromId);
            catalog[toId] = definition;

            if (_moveDefinitions.TryGetValue((fromId, isFullBody), out MoveDefinitionViewModel? vm))
            {
                _moveDefinitions.Remove((fromId, isFullBody));
                vm.Id = toId;
                _moveDefinitions[(toId, isFullBody)] = vm;
            }

            foreach (MoveClipViewModel clip in affected)
                clip.MoveId = toId;

            OnPropertyChanged(isFullBody ? nameof(AvailableFullBodyCoachMoves) : nameof(AvailableHandCoachMoves));
            OnPropertyChanged(nameof(Tracks));
        }

        ApplyRename(oldId, newId);

        PushUndo(
            undo: () => ApplyRename(newId, oldId),
            redo: () => ApplyRename(oldId, newId)
        );

        return true;
    }

    public IReadOnlyList<PictogramClipViewModel> SelectAllPictogramInstances(string pictogramId)
    {
        pictogramId = pictogramId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pictogramId))
            return [];

        List<PictogramClipViewModel> matches = [.. Tracks
            .SelectMany(t => t.Clips)
            .OfType<PictogramClipViewModel>()
            .Where(c => string.Equals(c.PictogramId, pictogramId, StringComparison.OrdinalIgnoreCase))];

        HashSet<PictogramClipViewModel> selected = [.. matches];
        foreach (ClipViewModel clip in Tracks.SelectMany(t => t.Clips))
            clip.IsSelected = clip is PictogramClipViewModel pc && selected.Contains(pc);

        return matches;
    }

    public IReadOnlyList<MoveClipViewModel> SelectAllMoveInstances(string moveId)
    {
        moveId = moveId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moveId))
            return [];

        List<MoveClipViewModel> matches = [.. Tracks
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()
            .Where(c => string.Equals(c.MoveId, moveId, StringComparison.OrdinalIgnoreCase))];

        HashSet<MoveClipViewModel> selected = [.. matches];
        foreach (ClipViewModel clip in Tracks.SelectMany(t => t.Clips))
            clip.IsSelected = clip is MoveClipViewModel mc && selected.Contains(mc);

        return matches;
    }

    public async Task RegeneratePictogramAsync(PictogramClipViewModel clip, PictogramScreenshotOptionsResult options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(VideoPath) || !File.Exists(VideoPath))
            return;

        string pictogramId = clip.PictogramId;
        if (string.IsNullOrWhiteSpace(pictogramId))
            return;

        string outputPath = ResolvePictogramPath(pictogramId);
        PixelSize targetSize = TimelinePictogramGenerator.GetTargetSize(CoachCount);
        double videoTimestamp = GetVideoTimestampSecondsFromBeat(clip.StartBeat);

        PictogramImageGenerator imageGenerator = new();
        await imageGenerator.GenerateVideoFramePictogramAsync(
            VideoPath,
            videoTimestamp,
            outputPath,
            targetSize,
            options.FrameLayoutMode,
            options.HorizontalFocus,
            cancellationToken);

        ImageBitmapCache.Invalidate(outputPath);
        if (!string.IsNullOrWhiteSpace(clip.ImagePath))
            ImageBitmapCache.Invalidate(clip.ImagePath);

        OnPropertyChanged(nameof(AvailablePictograms));
        OnPropertyChanged(nameof(Tracks));
    }

    private double GetVideoTimestampSecondsFromBeat(double beatLabel)
    {
        double playbackSeconds = GetPlaybackSecondsAtBeatLabel(beatLabel);
        double songStartOffset = TimelineStructure.GetSongStartOffset();
        return Math.Max(0, playbackSeconds + songStartOffset + VideoOffset);
    }

    private double ClampBeatForDuration(double beat, int durationFrames)
    {
        double minBeat = TimelineStructure?.StartBeat ?? 0;
        double maxBeat = TimelineStructure?.EndBeat ?? double.MaxValue;
        return Math.Max(minBeat, Math.Min(beat, maxBeat - (durationFrames / 24d)));
    }

    private string ResolvePictogramPath(string pictogramId)
    {
        string directory = Path.Combine(RootPath, "assets", "pictograms");
        string[] preferred = [".webp", ".png", ".jpg", ".jpeg"];

        foreach (string ext in preferred)
        {
            string candidate = Path.Combine(directory, pictogramId + ext);
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, pictogramId + ".webp");
    }

    private static string EnsureUniquePictogramId(string baseId, ISet<string> usedIds)
    {
        if (!usedIds.Contains(baseId))
            return baseId;

        int suffix = 2;
        while (true)
        {
            string candidate = $"{baseId}_{suffix}";
            if (!usedIds.Contains(candidate))
                return candidate;

            suffix++;
        }
    }

    private void HookUndoServiceStateChanged()
    {
        UndoService?.StateChanged += (s, e) =>
        {
            // Update title with unsaved indicator
            Title = UndoService.IsDirty ? $"{_baseTitle} *" : _baseTitle;

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

    public bool CanPasteCopiedClips => _clipClipboard.Count > 0;

    [RelayCommand]
    public void CopySelectedClips()
    {
        List<(int TrackIndex, ClipViewModel Clip)> selected = GetSelectedClipEntries();
        if (selected.Count == 0)
            return;

        double earliestStartBeat = selected.Min(s => s.Clip.StartBeat);
        List<ClipClipboardEntry> clipboard = [];

        foreach ((int trackIndex, ClipViewModel clip) in selected)
        {
            ClipClipboardPayload? payload = CreateClipboardPayload(clip);
            if (payload == null)
                continue;

            TrackViewModel sourceTrack = Tracks[trackIndex];
            clipboard.Add(new ClipClipboardEntry(
                trackIndex,
                sourceTrack.TrackType,
                sourceTrack.Title,
                clip.StartBeat - earliestStartBeat,
                payload));
        }

        if (clipboard.Count == 0)
            return;

        _clipClipboard.Clear();
        _clipClipboard.AddRange(clipboard);

        try
        {
            PasteCopiedClipsCommand.NotifyCanExecuteChanged();
        }
        catch { }
    }

    [RelayCommand(CanExecute = nameof(CanPasteCopiedClips))]
    public void PasteCopiedClips()
    {
        if (_clipClipboard.Count == 0)
            return;

        List<ClipViewModel> previousSelection = [.. Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected)];

        // Align the first copied clip start to the current playhead beat.
        double pasteAnchor = double.IsFinite(CurrentBeat) ? CurrentBeat : TimelineStructure.StartBeat;

        List<(TrackViewModel Track, ClipViewModel Clip)> inserted = [];
        foreach (ClipClipboardEntry entry in _clipClipboard)
        {
            TrackViewModel? targetTrack = ResolvePasteTrack(entry);
            if (targetTrack == null)
                continue;

            ClipViewModel created = entry.Payload.CreateViewModel(this, pasteAnchor + entry.RelativeStartBeat);
            created.IsSelected = false;

            targetTrack.Clips.Add(created);
            inserted.Add((targetTrack, created));
        }

        if (inserted.Count == 0)
            return;

        List<ClipViewModel> insertedClips = [.. inserted.Select(i => i.Clip)];

        PushUndo(
            undo: () =>
            {
                foreach ((TrackViewModel track, ClipViewModel clip) in inserted)
                {
                    track.Clips.Remove(clip);
                }

                SetSelectedClips(previousSelection);
            },
            redo: () =>
            {
                foreach ((TrackViewModel track, ClipViewModel clip) in inserted)
                {
                    if (!track.Clips.Contains(clip))
                        track.Clips.Add(clip);
                }

                SetSelectedClips(insertedClips);
            }
        );

        SetSelectedClips(insertedClips);
    }

    private List<(int TrackIndex, ClipViewModel Clip)> GetSelectedClipEntries()
    {
        List<(int TrackIndex, ClipViewModel Clip)> selected = [];
        for (int i = 0; i < Tracks.Count; i++)
        {
            foreach (ClipViewModel clip in Tracks[i].Clips.Where(c => c.IsSelected))
                selected.Add((i, clip));
        }

        selected.Sort(static (a, b) =>
        {
            int trackCompare = a.TrackIndex.CompareTo(b.TrackIndex);
            return trackCompare != 0 ? trackCompare : a.Clip.StartBeat.CompareTo(b.Clip.StartBeat);
        });

        return selected;
    }

    private static KaraokeTolerance? CloneTolerance(KaraokeTolerance? tolerance)
    {
        if (tolerance == null)
            return null;

        return new KaraokeTolerance
        {
            StartTimeTolerance = tolerance.StartTimeTolerance,
            EndTimeTolerance = tolerance.EndTimeTolerance,
            SemitoneTolerance = tolerance.SemitoneTolerance
        };
    }

    private static ClipClipboardPayload? CreateClipboardPayload(ClipViewModel clip)
    {
        return clip switch
        {
            PictogramClipViewModel pictogram => new PictogramClipboardPayload(
                pictogram.PictogramId,
                ((PictogramClip)pictogram.RawClip).Duration,
                ((PictogramClip)pictogram.RawClip).CoachCount),
            KaraokeClipViewModel karaoke => new KaraokeClipboardPayload(
                karaoke.Lyrics,
                ((KaraokeClip)karaoke.RawClip).Duration,
                ((KaraokeClip)karaoke.RawClip).Pitch,
                karaoke.IsEndOfLine,
                ((KaraokeClip)karaoke.RawClip).ContentType,
                CloneTolerance(((KaraokeClip)karaoke.RawClip).Tolerances)),
            HideUserInterfaceClipViewModel hideHud => new HideHudClipboardPayload(
                ((HideUserInterfaceClip)hideHud.RawClip).Duration,
                ((HideUserInterfaceClip)hideHud.RawClip).IsActive),
            GoldEffectClipViewModel gold => new GoldEffectClipboardPayload(
                ((GoldEffectClip)gold.RawClip).TrackId,
                ((GoldEffectClip)gold.RawClip).Duration,
                ((GoldEffectClip)gold.RawClip).IsActive,
                ((GoldEffectClip)gold.RawClip).EffectType),
            MoveClipViewModel move => new MoveClipboardPayload(move.MoveId, move.IsGoldMove, move.IsFullBody),
            _ => null
        };
    }

    private TrackViewModel? ResolvePasteTrack(ClipClipboardEntry entry)
    {
        if (entry.TrackIndex >= 0 && entry.TrackIndex < Tracks.Count)
        {
            TrackViewModel sameIndexTrack = Tracks[entry.TrackIndex];
            if (sameIndexTrack.TrackType == entry.TrackType)
                return sameIndexTrack;
        }

        return Tracks.FirstOrDefault(t => t.TrackType == entry.TrackType && t.Title == entry.TrackTitle)
            ?? Tracks.FirstOrDefault(t => t.TrackType == entry.TrackType);
    }

    private void SetSelectedClips(IEnumerable<ClipViewModel> selected)
    {
        HashSet<ClipViewModel> selectedSet = [.. selected];
        foreach (ClipViewModel clip in Tracks.SelectMany(t => t.Clips))
            clip.IsSelected = selectedSet.Contains(clip);

        if (Application.Current is App app)
        {
            app.TimelineContext.SelectedObjects = [.. Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>()];
        }
    }

    private ClipViewModel ClampAndCreate(ClipViewModel clip, double desiredStartBeat)
    {
        double minBeat = TimelineStructure.StartBeat;
        double maxStartBeat = TimelineStructure.EndBeat - clip.DurationBeats;
        if (maxStartBeat < minBeat)
            maxStartBeat = minBeat;

        double clampedStartBeat = Math.Max(minBeat, Math.Min(desiredStartBeat, maxStartBeat));
        clip.StartBeat = clampedStartBeat;
        return clip;
    }

    private sealed record ClipClipboardEntry(int TrackIndex, TrackType TrackType, string TrackTitle, double RelativeStartBeat, ClipClipboardPayload Payload);

    private abstract record ClipClipboardPayload
    {
        public abstract ClipViewModel CreateViewModel(TimelineEditorViewModel timeline, double startBeat);
    }

    private sealed record PictogramClipboardPayload(string PictogramId, int DurationFrames, int CoachCount) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineEditorViewModel timeline, double startBeat)
        {
            PictogramClip raw = new()
            {
                PictogramId = PictogramId,
                Duration = DurationFrames,
                CoachCount = CoachCount,
                StartTime = 0
            };

            return timeline.ClampAndCreate(new PictogramClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record KaraokeClipboardPayload(string Lyrics, int DurationFrames, float Pitch, bool IsEndOfLine, int ContentType, KaraokeTolerance? Tolerance) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineEditorViewModel timeline, double startBeat)
        {
            KaraokeClip raw = new()
            {
                Lyrics = Lyrics,
                Duration = DurationFrames,
                Pitch = Pitch,
                IsEndOfLine = IsEndOfLine,
                ContentType = ContentType,
                Tolerances = CloneTolerance(Tolerance),
                StartTime = 0
            };

            return timeline.ClampAndCreate(new KaraokeClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record HideHudClipboardPayload(int DurationFrames, bool IsActive) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineEditorViewModel timeline, double startBeat)
        {
            HideUserInterfaceClip raw = new()
            {
                Duration = DurationFrames,
                IsActive = IsActive,
                StartTime = 0
            };

            return timeline.ClampAndCreate(new HideUserInterfaceClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record GoldEffectClipboardPayload(long TrackId, int DurationFrames, bool IsActive, int EffectType) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineEditorViewModel timeline, double startBeat)
        {
            GoldEffectClip raw = new()
            {
                TrackId = TrackId,
                Duration = DurationFrames,
                IsActive = IsActive,
                EffectType = EffectType,
                StartTime = 0
            };

            return timeline.ClampAndCreate(new GoldEffectClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record MoveClipboardPayload(string MoveId, bool IsGoldMove, bool IsFullBody) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineEditorViewModel timeline, double startBeat)
        {
            MoveClip raw = new()
            {
                MoveId = MoveId,
                IsGoldMove = IsGoldMove,
                StartTime = 0
            };

            MoveClipViewModel vm = new(raw, timeline.RootPath, timeline, IsFullBody);
            return timeline.ClampAndCreate(vm, startBeat);
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
        if (UndoService.IsDirty && !_allowClose)
        {
            // Cancel this close attempt and show a save-prompt asynchronously.
            // When the user confirms, _allowClose is set and Close() is re-triggered.
            Dispatcher.UIThread.InvokeAsync(PromptSaveOnCloseAsync);
            return false;
        }

        Playback.Dispose();

        return base.OnClose();
    }

    internal async Task<bool> TrySaveAndReportFailureAsync(Window? owner)
    {
        try
        {
            Save();
            return true;
        }
        catch (Exception ex)
        {
            await ShowSaveErrorAsync(owner, ex);
            return false;
        }
    }

    internal async Task<bool> TrySaveAndCloseAfterPromptAsync(Window? owner)
    {
        if (!await TrySaveAndReportFailureAsync(owner))
            return false;

        CloseAfterConfirmedPrompt();
        return true;
    }

    internal static async Task ShowSaveErrorAsync(Window? owner, Exception exception)
    {
        Debug.WriteLine($"Failed to save map: {exception}");

        if (owner == null)
            return;

        Window errorWin = new()
        {
            Title = "Error Saving Map",
            Width = 420,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = $"Failed to save map:\n{exception.Message}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };

        await errorWin.ShowDialog(owner);
    }

    private void CloseAfterConfirmedPrompt()
    {
        _allowClose = true;
        (Owner as IDock)?.Factory?.CloseDockable(this);
    }

    /// <summary>
    /// Shows a "Save / Don't Save / Cancel" dialog when the user tries to close a
    /// dirty tab. On confirmation the dockable is closed programmatically.
    /// </summary>
    private async Task PromptSaveOnCloseAsync()
    {
        Window? mainWindow =
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime al
                ? al.MainWindow
                : null;
        if (mainWindow == null)
            return;

        Button saveBtn = new() { Content = "Save", Width = 96, Margin = new Thickness(6) };
        Button discardBtn = new() { Content = "Don't Save", Width = 96, Margin = new Thickness(6) };
        Button cancelBtn = new() { Content = "Cancel", Width = 96, Margin = new Thickness(6) };

        StackPanel buttons = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        buttons.Children.AddRange([saveBtn, discardBtn, cancelBtn]);

        StackPanel body = new() { Margin = new Thickness(20, 16, 20, 12) };
        body.Children.Add(new TextBlock
        {
            Text = $"\"{_baseTitle}\" has unsaved changes. Do you want to save before closing?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        body.Children.Add(buttons);

        Window dialog = new()
        {
            Title = "Unsaved Changes",
            Width = 440,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body
        };

        string choice = "cancel";
        saveBtn.Click += (_, _) =>
        {
            choice = "save";
            dialog.Close();
        };
        discardBtn.Click += (_, _) =>
        {
            choice = "discard";
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(mainWindow);

        if (choice == "save")
        {
            await TrySaveAndCloseAfterPromptAsync(mainWindow);
            return;
        }

        if (choice == "discard")
            CloseAfterConfirmedPrompt();
    }

    partial void OnSnapToGridChanged(bool value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _settings.SnapToGrid = value;
    }

    partial void OnSnapToCurrentTimeMarkerChanged(bool value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _settings.SnapToCurrentTimeMarker = value;
    }

    partial void OnSnapGridSizeChanged(double value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _settings.SnapGridSize = value;
    }

    partial void OnSnapThresholdChanged(double value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _settings.SnapThreshold = value;
    }

    partial void OnSnapToClipsChanged(bool value)
    {
        if (_suppressSnapBroadcast)
        {
            _suppressSnapBroadcast = false;
            return;
        }

        _settings.SnapToClips = value;
    }
}