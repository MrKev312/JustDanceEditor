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
    private readonly TimelineMediaController _media;
    private readonly TimelineClipboardController _clipboard;
    private readonly TimelineSelectionController _selection;
    private readonly TimelineStructureEditorController _structureEditor;
    private readonly TimelinePictogramActions _pictogramActions;
    private readonly TimelineMoveDefinitionController _moveDefinitionController;
    private readonly TimelineLyricsController _lyrics;
    private readonly TimelineSnapSettingsController _snapSettings;
    private readonly TimelineMetronomeController _metronome;
    private readonly TimelineCloseController _closeController;

    public IntermediateSongPackage Package { get; }

    public string RootPath { get; }

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
    public partial double RequestedCenterBeat { get; set; } = double.NaN;

    [ObservableProperty]
    public partial int CenterBeatRequestVersion { get; set; }

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

    [ObservableProperty]
    public partial double AudioStartBeat { get; set; }

    [ObservableProperty]
    public partial double AudioEndBeat { get; set; } = double.MaxValue;

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

    [ObservableProperty]
    public partial bool IsMediaLoading { get; set; } = false;

    [ObservableProperty]
    public partial string? MediaLoadError { get; set; }
    internal string BaseTitle { get; set; } = string.Empty;

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];
    public IPlaybackService Playback { get; }
    public TimelineStructureDocument TimelineStructure => Package.TimelineStructure;

    // Registry of known move definitions keyed by (id,isFullBody)
    private readonly Dictionary<(string id, bool isFullBody), MoveDefinitionViewModel> _moveDefinitions = [];
    public IReadOnlyDictionary<(string id, bool isFullBody), MoveDefinitionViewModel> MoveDefinitions => _moveDefinitions;

    public string AudioPath { get; internal set; } = "";
    public string VideoPath { get; internal set; } = "";
    public PcmWaveAudioData? PreparedAudio { get; internal set; }
    public double StartBeatValue { get; internal set; }
    public double VideoOffset { get; internal set; }
    public double VideoDurationSeconds { get; internal set; }
    public int CoachCount => Package.Metadata.CoachCount;
    public string LyricsColor => Package.Metadata.LyricsColor;

    public IEnumerable<string> AvailableHandCoachMoves => Package.HandCoachMoves.Keys;
    public IEnumerable<string> AvailableFullBodyCoachMoves => Package.FullBodyCoachMoves.Keys;

    public bool TryGetCoachMoveColor(string moveId, out Color color) => TimelineAssetLookup.TryGetCoachMoveColor(this, moveId, out color);

    public IEnumerable<string> AvailablePictograms => TimelineAssetLookup.GetAvailablePictograms(RootPath);

    public TimelineEditorViewModel(
        IntermediateSongPackage package,
        string rootPath,
        IPlaybackService playback,
        TimelineSettingsService settings,
        ITimelinePictogramGenerator? pictogramGenerator = null)
    {
        Package = package;
        RootPath = rootPath;
        _media = new TimelineMediaController(this);
        _clipboard = new TimelineClipboardController(this);
        _selection = new TimelineSelectionController(this);
        _structureEditor = new TimelineStructureEditorController(this);
        _pictogramActions = new TimelinePictogramActions(this, pictogramGenerator);
        _moveDefinitionController = new TimelineMoveDefinitionController(this, _moveDefinitions);
        _lyrics = new TimelineLyricsController(this);
        _metronome = new TimelineMetronomeController(this);
        _closeController = new TimelineCloseController(this, () => BaseTitle);
        Id = package.Metadata.SongID.ToString();
        BaseTitle = package.Metadata.MapName;
        Title = BaseTitle;

        // Create undo service for this timeline
        UndoService = new UndoService();
        HookUndoServiceStateChanged();

        Playback = playback;
        Playback.TimeChanged += (s, e) => CurrentBeat = Playback.CurrentBeat;

        BeatOffset = Package.TimelineStructure.StartBeat;
        MaxBeat = Package.TimelineStructure.EndBeat - Package.TimelineStructure.StartBeat;
        UpdateTimelineWidth();

        BuildTimeline();

        _snapSettings = new TimelineSnapSettingsController(this, settings);
        _snapSettings.Initialize();
    }

    public Task InitializeAsync() => _media.InitializeAsync();

    internal void BuildTimeline()
    {
        _lyrics.SetDefinitionColor(TimelineTrackBuilder.Build(this, _moveDefinitions), updateMetadata: true);

        _media.SyncVideoTrackClip();
        PreloadPictogramImages();
    }

    private void PreloadPictogramImages()
    {
        TrackViewModel? pictogramTrack = Tracks.FirstOrDefault(t => t.TrackType == TrackType.Pictogram);
        if (pictogramTrack == null)
            return;

        SkiaPictogramImageCache.Preload(
            pictogramTrack.Clips
                .OfType<PictogramClipViewModel>()
                .Select(clip => clip.ImagePath));
    }

    internal void NotifyPictogramAssetsChanged()
    {
        OnPropertyChanged(nameof(AvailablePictograms));
        OnPropertyChanged(nameof(Tracks));
    }

    internal void NotifyAvailableMovesChanged(bool isFullBody)
    {
        OnPropertyChanged(isFullBody
            ? nameof(AvailableFullBodyCoachMoves)
            : nameof(AvailableHandCoachMoves));
    }

    internal void NotifyTracksChanged()
    {
        OnPropertyChanged(nameof(Tracks));
    }

    internal void NotifyMediaPathChanged()
    {
        OnPropertyChanged(nameof(VideoPath));
        OnPropertyChanged(nameof(VideoDurationSeconds));
    }

    internal void NotifyVideoOffsetChanged()
    {
        OnPropertyChanged(nameof(VideoOffset));
    }

    internal void NotifyRebuiltTimelineStructure()
    {
        OnPropertyChanged(nameof(StartBeatValue));
        OnPropertyChanged(nameof(VideoOffset));
        OnPropertyChanged(nameof(TimelineStructure));
    }

    internal void NotifyLyricsColorChanged()
    {
        OnPropertyChanged(nameof(LyricsColor));
        OnPropertyChanged(nameof(LyricsDefinitionColor));
    }

    internal void ClearMoveDefinitions()
    {
        _moveDefinitions.Clear();
    }

    public Task RebuildFromPackageAsync() => _media.RebuildFromPackageAsync();

    public double GetPlaybackSecondsAtBeatLabel(double beatLabel) => _media.GetPlaybackSecondsAtBeatLabel(beatLabel);

    public double GetBeatLabelAtPlaybackSeconds(double seconds) => _media.GetBeatLabelAtPlaybackSeconds(seconds);

    public void SetVideoOffsetFromClipStartBeat(double startBeat) => _media.SetVideoOffsetFromClipStartBeat(startBeat);

    public void SetVideoOffset(double offsetSeconds) => _media.SetVideoOffset(offsetSeconds);

    public MoveDefinitionViewModel GetOrRegisterMove(string moveId, bool isFullBody) => _moveDefinitionController.GetOrRegisterMove(moveId, isFullBody);

    public void RefreshMoveAssetStatus() => _moveDefinitionController.RefreshMoveAssetStatus();

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
        => _moveDefinitionController.RegisterNewMoveDefinition(id, isFullBody, durationFrames, color);

    /// <summary>
    /// Persist current timeline state back into the intermediate package and write it to disk.
    /// Ensures shared definitions (moves, lyrics color) are synchronized into the package before saving.
    /// </summary>
    public void Save() => TimelinePackageSaver.Save(this, _moveDefinitions, BaseTitle);

    internal void UpdateTimelineWidth()
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
    private void Undo() => UndoService.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoService.Redo();

    public Task GeneratePictogramsAsync(PictogramGenerationMode mode, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default)
        => _pictogramActions.GeneratePictogramsAsync(mode, frameLayoutMode, horizontalFocus, cancellationToken);

    public Task GenerateNewPictogramAsync(double playheadBeat, PictogramScreenshotOptionsResult options, CancellationToken cancellationToken = default)
        => _pictogramActions.GenerateNewPictogramAsync(playheadBeat, options, cancellationToken);

    public static List<double> BuildPictogramInsertionBeats(IEnumerable<MoveClipViewModel> moveClips, double playheadBeat, PictogramScreenshotOptionsResult options)
    {
        return TimelinePictogramActions.BuildPictogramInsertionBeats(moveClips, playheadBeat, options);
    }

    public static string BuildNewPictogramBaseId(PictogramScreenshotOptionsResult options)
    {
        return TimelinePictogramActions.BuildNewPictogramBaseId(options);
    }

    public bool RenamePictogramId(string oldId, string newId) => _pictogramActions.RenamePictogramId(oldId, newId);

    public Task<bool> FlipPictogramAsync(string pictogramId, PictogramClipViewModel? specificClip = null, CancellationToken cancellationToken = default)
        => _pictogramActions.FlipPictogramAsync(pictogramId, specificClip, cancellationToken);

    public bool RenameMoveId(string oldId, string newId, bool isFullBody) => _moveDefinitionController.RenameMoveId(oldId, newId, isFullBody);

    public IReadOnlyList<PictogramClipViewModel> SelectAllPictogramInstances(string pictogramId) => _selection.SelectAllPictogramInstances(pictogramId);

    public IReadOnlyList<MoveClipViewModel> SelectAllMoveInstances(string moveId) => _selection.SelectAllMoveInstances(moveId);

    public Task RegeneratePictogramAsync(PictogramClipViewModel clip, PictogramScreenshotOptionsResult options, CancellationToken cancellationToken = default)
        => _pictogramActions.RegeneratePictogramAsync(clip, options, cancellationToken);

    private void HookUndoServiceStateChanged()
    {
        UndoService?.StateChanged += (s, e) =>
        {
            // Update title with unsaved indicator
            Title = UndoService.IsDirty ? $"{BaseTitle} *" : BaseTitle;

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
    public void DeleteSelectedClips() => _clipboard.DeleteSelectedClips();

    public bool CanPasteCopiedClips => _clipboard.CanPasteCopiedClips;

    [RelayCommand]
    public void CopySelectedClips()
    {
        _clipboard.CopySelectedClips();
        PasteCopiedClipsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPasteCopiedClips))]
    public void PasteCopiedClips() => _clipboard.PasteCopiedClips();

    public void SelectAndCenterClip(ClipViewModel clip) => _clipboard.SelectAndCenterClip(clip);

    public void PushUndo(Action undo, Action redo)
    {
        if (undo == null || redo == null)
            return;
        UndoService.Record(undo, redo);
    }

    public void UpdateLyricsColor(string rgbaHexColor) => _lyrics.UpdateLyricsColor(rgbaHexColor);

    public Color LyricsDefinitionColor
    {
        get => _lyrics.DefinitionColor;
        set => _lyrics.SetDefinitionColor(value, updateMetadata: true);
    }

    public string GetLyricsColor() => LyricsColor;

    // ─── Section / Signature editing ───

    public void AddSection(double beat, SongSectionType type) => _structureEditor.AddSection(beat, type);

    public void RemoveSection(SectionSegment section) => _structureEditor.RemoveSection(section);

    public void MoveSection(SectionSegment section, double newBeat) => _structureEditor.MoveSection(section, newBeat);

    public void ChangeSectionType(SectionSegment section, SongSectionType newType) => _structureEditor.ChangeSectionType(section, newType);

    public void AddSignature(double beat, int beats) => _structureEditor.AddSignature(beat, beats);

    public void RemoveSignature(SignatureSegment sig) => _structureEditor.RemoveSignature(sig);

    public void MoveSignature(SignatureSegment sig, double newBeat) => _structureEditor.MoveSignature(sig, newBeat);

    public void ChangeSignatureBeats(SignatureSegment sig, int newBeats) => _structureEditor.ChangeSignatureBeats(sig, newBeats);

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
        _metronome.ApplyEnabled(value);
    }

    internal void UpdateMetronomeTiming() => _metronome.UpdateTiming();

    public override bool OnClose()
    {
        return _closeController.ShouldClose() && base.OnClose();
    }

    internal Task<bool> TrySaveAndReportFailureAsync(Window? owner) => _closeController.TrySaveAndReportFailureAsync(owner);

    internal Task<bool> TrySaveAndCloseAfterPromptAsync(Window? owner) => _closeController.TrySaveAndCloseAfterPromptAsync(owner);

    internal static Task ShowSaveErrorAsync(Window? owner, Exception exception) => TimelineCloseController.ShowSaveErrorAsync(owner, exception);

    partial void OnSnapToGridChanged(bool value) => _snapSettings.SetSnapToGrid(value);

    partial void OnSnapToCurrentTimeMarkerChanged(bool value) => _snapSettings.SetSnapToCurrentTimeMarker(value);

    partial void OnSnapGridSizeChanged(double value) => _snapSettings.SetSnapGridSize(value);

    partial void OnSnapThresholdChanged(double value) => _snapSettings.SetSnapThreshold(value);

    partial void OnSnapToClipsChanged(bool value) => _snapSettings.SetSnapToClips(value);
}