using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

/// <summary>
/// Result returned by the Edit Song dialog when the user confirms changes.
/// Contains all the information needed to update the existing package's structure.
/// </summary>
public class EditSongResult
{
    public required string MapName { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required int CoachCount { get; init; }
    public required uint Difficulty { get; init; }
    public required double Bpm { get; init; }
    public required int BeatsPerMeasure { get; init; }
    public required double ZeroBeatTimeSeconds { get; init; }
    public required int StartBeat { get; init; }
    public required int EndBeat { get; init; }
    public required ObservableCollection<SectionEntry> Sections { get; init; }
}

/// <summary>
/// ViewModel for the Edit Song dialog.
/// Allows editing metadata and beat alignment of an already-opened song.
/// Uses a 2-step wizard:
///   Step 0: Basic metadata (map name, title, artist, coach count, difficulty)
///   Step 1: Beat alignment (waveform zero-beat, BPM, beats per measure, start/end beat)
/// </summary>
public partial class EditSongViewModel : ObservableObject, IDialogResult<EditSongResult>, ISongEditorViewModel, IDisposable
{
    // Step tracking
    public const int TotalSteps = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep0))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowPlaybackControls))]
    public partial int CurrentStep { get; set; }

    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsLastStep => CurrentStep == TotalSteps - 1;
    public bool CanGoBack => CurrentStep > 0;
    public string NextButtonText => IsLastStep ? "Apply" : "Next";
    public bool ShowPlaybackControls => CurrentStep is 1;

    // --- Step 0: Metadata ---
    [ObservableProperty]
    public partial string MapName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SongTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Artist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int CoachCount { get; set; } = 1;

    [ObservableProperty]
    public partial uint Difficulty { get; set; } = 1;

    // --- Step 1: Beat Alignment ---
    [ObservableProperty]
    public partial float[] WaveformSamples { get; set; } = [];

    [ObservableProperty]
    public partial double AudioDurationSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeroBeatTimeFormatted))]
    public partial double ZeroBeatTimeSeconds { get; set; }

    public string ZeroBeatTimeFormatted => $"{ZeroBeatTimeSeconds:F3}s";

    [ObservableProperty]
    public partial double Bpm { get; set; } = 120.0;

    [ObservableProperty]
    public partial int BeatsPerMeasure { get; set; } = 4;

    [ObservableProperty]
    public partial int StartBeat { get; set; } = -8;

    [ObservableProperty]
    public partial int EndBeat { get; set; } = 200;

    /// <summary>
    /// When beat 0 position changes, recalculate StartBeat/EndBeat and update metronome.
    /// </summary>
    partial void OnZeroBeatTimeSecondsChanged(double value)
    {
        RecalculateStartEndBeats();
        UpdateMetronome();
    }

    partial void OnBpmChanged(double value)
    {
        RecalculateStartEndBeats();
        UpdateMetronome();
    }

    partial void OnBeatsPerMeasureChanged(int value)
    {
        RecalculateStartEndBeats();
        UpdateMetronome();
    }

    private void RecalculateStartEndBeats()
    {
        if (Bpm <= 0 || BeatsPerMeasure <= 0 || AudioDurationSeconds <= 0)
            return;

        double beatDuration = 60.0 / Bpm;
        int bpm = BeatsPerMeasure;

        double beatsBeforeZero = ZeroBeatTimeSeconds / beatDuration;
        int measuresBeforeZero = (int)Math.Ceiling(beatsBeforeZero / bpm) + 1;
        StartBeat = -(measuresBeforeZero * bpm);

        double beatsAfterZero = (AudioDurationSeconds - ZeroBeatTimeSeconds) / beatDuration;
        int measuresAfterZero = (int)Math.Ceiling(beatsAfterZero / bpm) + 1;
        EndBeat = measuresAfterZero * bpm;
    }

    // --- Sections (kept for result, edited in timeline) ---
    public ObservableCollection<SectionEntry> Sections { get; } = [];

    // --- Audio Preview Playback ---
    private SongPreviewPlayer? _previewPlayer;
    private DispatcherTimer? _playheadTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIcon))]
    public partial bool IsPreviewPlaying { get; set; }

    [ObservableProperty]
    public partial double PlayheadTimeSeconds { get; set; }

    public string PlayPauseIcon => IsPreviewPlaying ? "⏸" : "▶";

    // --- Dialog Result ---
    public EditSongResult? Result { get; private set; }

    // --- Source info ---
    private readonly string _audioPath;

    /// <summary>
    /// Initialize the edit dialog from the currently active timeline.
    /// </summary>
    public EditSongViewModel(TimelineEditorViewModel timeline)
    {
        IntermediateSongPackage package = timeline.Package;
        _audioPath = timeline.AudioPath;

        // Populate metadata
        MapName = package.Metadata.MapName;
        SongTitle = package.Metadata.Title;
        Artist = package.Metadata.Artist;
        CoachCount = package.Metadata.CoachCount;
        Difficulty = package.Metadata.Difficulty;

        // Derive BPM from markers
        TimelineStructureDocument ts = package.TimelineStructure;
        if (ts.Markers.Count >= 2)
        {
            double beatDurationSec = (ts.Markers[1] - ts.Markers[0]) / 48000.0;
            if (beatDurationSec > 0)
                Bpm = Math.Round(60.0 / beatDurationSec, 1);
        }

        // Derive BeatsPerMeasure from signatures
        if (ts.Signatures.Count > 0)
        {
            BeatsPerMeasure = ts.Signatures[0].Beats;
        }

        // Derive ZeroBeatTime from markers
        // markers[0] corresponds to beat StartBeat; beat 0 is at index -StartBeat
        int zeroBeatIndex = -ts.StartBeat;
        if (zeroBeatIndex >= 0 && zeroBeatIndex < ts.Markers.Count)
            ZeroBeatTimeSeconds = ts.Markers[zeroBeatIndex] / 48000.0;

        StartBeat = ts.StartBeat;
        EndBeat = ts.EndBeat;

        // Copy existing sections
        foreach (SectionSegment section in ts.Sections.OrderBy(s => s.StartBeat))
        {
            Sections.Add(new SectionEntry
            {
                SectionType = section.SectionType,
                StartBeat = section.StartBeat
            });
        }

        // Use existing waveform samples from the timeline
        WaveformSamples = timeline.WaveformSamples;

        // Load audio duration and preview player
        _ = InitializeAudioAsync();
    }

    private async Task InitializeAudioAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_audioPath) && File.Exists(_audioPath))
            {
                AudioDurationSeconds = await AudioConversionService.GetDurationAsync(_audioPath);

                _previewPlayer = new SongPreviewPlayer();
                await _previewPlayer.LoadAsync(_audioPath);
                UpdateMetronome();

                _playheadTimer ??= new DispatcherTimer(
                    TimeSpan.FromMilliseconds(16),
                    DispatcherPriority.Render,
                    (_, _) =>
                    {
                        if (_previewPlayer != null)
                        {
                            PlayheadTimeSeconds = _previewPlayer.CurrentTime.TotalSeconds;
                            if (!_previewPlayer.IsPlaying && IsPreviewPlaying)
                            {
                                IsPreviewPlaying = false;
                                _playheadTimer?.Stop();
                            }
                        }
                    });
            }
        }
        catch (Exception ex)
        {
            EditorLog.Fallback(ex, $"Load edit-song waveform '{_audioPath}'");
            AudioDurationSeconds = 0;
        }
    }

    // --- Commands ---
    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    [RelayCommand]
    private void GoNext()
    {
        if (CurrentStep < TotalSteps - 1)
            CurrentStep++;
    }

    /// <summary>
    /// Sets the playback position.
    /// </summary>
    public void SeekTo(double timeSeconds)
    {
        _previewPlayer?.Seek(TimeSpan.FromSeconds(timeSeconds));
        PlayheadTimeSeconds = timeSeconds;
    }

    [RelayCommand]
    private void TogglePreview()
    {
        if (_previewPlayer == null || !_previewPlayer.IsLoaded)
            return;

        if (IsPreviewPlaying)
        {
            _previewPlayer.Pause();
        }
        else
        {
            UpdateMetronome();
            _previewPlayer.Play();
        }

        IsPreviewPlaying = _previewPlayer.IsPlaying;
        if (IsPreviewPlaying)
            _playheadTimer?.Start();
        else
            _playheadTimer?.Stop();
    }

    [RelayCommand]
    private void StopPreview()
    {
        _previewPlayer?.Stop();
        IsPreviewPlaying = false;
        PlayheadTimeSeconds = 0;
        _playheadTimer?.Stop();
    }

    private void UpdateMetronome()
    {
        _previewPlayer?.UpdateMetronome(ZeroBeatTimeSeconds, Bpm, BeatsPerMeasure, Sections);
    }

    public void Accept()
    {
        StopPreview();

        Result = new EditSongResult
        {
            MapName = MapName.Trim(),
            Title = SongTitle.Trim(),
            Artist = Artist.Trim(),
            CoachCount = CoachCount,
            Difficulty = Difficulty,
            Bpm = Bpm,
            BeatsPerMeasure = BeatsPerMeasure,
            ZeroBeatTimeSeconds = ZeroBeatTimeSeconds,
            StartBeat = StartBeat,
            EndBeat = EndBeat,
            Sections = Sections,
        };
    }

    public void Cancel()
    {
        StopPreview();
        Result = null;
    }

    public void Dispose()
    {
        _playheadTimer?.Stop();
        _previewPlayer?.Dispose();
        _previewPlayer = null;
        GC.SuppressFinalize(this);
    }
}
