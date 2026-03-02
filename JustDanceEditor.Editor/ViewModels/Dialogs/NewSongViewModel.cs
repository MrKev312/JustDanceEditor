using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

/// <summary>
/// Result returned by the NewSong dialog when the user confirms creation.
/// Contains all the information needed to build an IntermediateSongPackage.
/// </summary>
public class NewSongResult
{
    public required string MapName { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required int CoachCount { get; init; }
    public required uint Difficulty { get; init; }
    public required double Bpm { get; init; }
    public required int BeatsPerMeasure { get; init; }
    public required string AudioFilePath { get; init; }
    public required double ZeroBeatTimeSeconds { get; init; }
    public required int StartBeat { get; init; }
    public required int EndBeat { get; init; }
    public required ObservableCollection<SectionEntry> Sections { get; init; }
    public required string OutputFolder { get; init; }
}
/// <summary>
/// ViewModel for the New Song creation dialog.
/// Uses a multi-step wizard approach:
///   Step 0: Basic metadata (map name, title, artist, coach count, difficulty)
///   Step 1: Audio &amp; timing (import audio, set BPM, beats per measure)
///   Step 2: Beat alignment (select the 0th beat in the waveform, set padding)
///   Step 3: Save location
/// </summary>
public partial class NewSongViewModel : ObservableObject, IDialogResult<NewSongResult>, ISongEditorViewModel, IDisposable
{
    // Step tracking
    public const int TotalSteps = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep0))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowPlaybackControls))]
    public partial int CurrentStep { get; set; }

    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool CanGoBack => CurrentStep > 0;
    public bool IsLastStep => CurrentStep == TotalSteps - 1;
    public string NextButtonText => IsLastStep ? "Create" : "Next";
    public bool ShowPlaybackControls => CurrentStep is 2;

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

    // --- Step 1: Audio & Timing ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudio))]
    [NotifyPropertyChangedFor(nameof(AudioFileName))]
    public partial string AudioFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Bpm { get; set; } = 120.0;

    [ObservableProperty]
    public partial int BeatsPerMeasure { get; set; } = 4;

    [ObservableProperty]
    public partial float[] WaveformSamples { get; set; } = [];

    [ObservableProperty]
    public partial double AudioDurationSeconds { get; set; }

    public bool HasAudio => !string.IsNullOrEmpty(AudioFilePath) && WaveformSamples.Length > 0;
    public string AudioFileName => string.IsNullOrEmpty(AudioFilePath) ? "(none)" : Path.GetFileName(AudioFilePath);

    // --- Step 2: Beat Alignment ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeroBeatTimeFormatted))]
    public partial double ZeroBeatTimeSeconds { get; set; }

    public string ZeroBeatTimeFormatted => $"{ZeroBeatTimeSeconds:F3}s";

    [ObservableProperty]
    public partial int StartBeat { get; set; } = -8;

    [ObservableProperty]
    public partial int EndBeat { get; set; } = 200;

    /// <summary>
    /// When beat 0 position changes, recalculate StartBeat/EndBeat to cover the
    /// entire song aligned to measure boundaries, with 1 measure padding on each side.
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

        // How many beats before beat 0 does the audio start?
        // Audio starts at t=0, beat 0 is at ZeroBeatTimeSeconds
        double beatsBeforeZero = ZeroBeatTimeSeconds / beatDuration;
        // Round up to the next measure boundary + 1 extra measure for padding
        int measuresBeforeZero = (int)Math.Ceiling(beatsBeforeZero / bpm) + 1;
        StartBeat = -(measuresBeforeZero * bpm);

        // How many beats from beat 0 to the end of the audio?
        double beatsAfterZero = (AudioDurationSeconds - ZeroBeatTimeSeconds) / beatDuration;
        // Round up to the next measure boundary + 1 extra measure for padding
        int measuresAfterZero = (int)Math.Ceiling(beatsAfterZero / bpm) + 1;
        EndBeat = measuresAfterZero * bpm;
    }

    // --- Sections (default, edited in timeline) ---
    public ObservableCollection<SectionEntry> Sections { get; } = [new SectionEntry { SectionType = SongSectionType.Verse, StartBeat = 0 }];

    // --- Step 3: Save Location ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutputFolder))]
    public partial string OutputFolder { get; set; } = string.Empty;

    public bool HasOutputFolder => !string.IsNullOrEmpty(OutputFolder);

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
    public NewSongResult? Result { get; private set; }

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
    /// Seeks the audio preview to the given time and updates the playhead.
    /// Called by waveform controls on Ctrl+click.
    /// </summary>
    public void SeekTo(double timeSeconds)
    {
        _previewPlayer?.Seek(TimeSpan.FromSeconds(timeSeconds));
        PlayheadTimeSeconds = timeSeconds;
    }

    // --- Playback Commands ---
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

    // --- File Browsing ---
    [RelayCommand]
    private async Task BrowseAudioAsync()
    {
        Window? topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null)
            return;

        System.Collections.Generic.IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Audio File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio Files") { Patterns = ["*.mp3", "*.wav", "*.ogg", "*.opus", "*.flac", "*.m4a", "*.aac", "*.wma"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count == 1)
        {
            string path = files[0].Path.LocalPath;
            AudioFilePath = path;
            await LoadWaveformAsync(path);
        }
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        Window? topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null)
            return;

        System.Collections.Generic.IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (folders.Count == 1)
        {
            OutputFolder = folders[0].Path.LocalPath;
        }
    }

    private async Task LoadWaveformAsync(string audioPath)
    {
        try
        {
            // Use AudioWaveformService to get waveform (via FFmpeg → raw s16le mono 8kHz)
            float[] samples = await AudioConversionService.GetWaveformDataAsync(audioPath);
            WaveformSamples = samples;

            // Get audio duration using FFmpeg (works with all formats including Opus)
            try
            {
                AudioDurationSeconds = await AudioConversionService.GetDurationAsync(audioPath);
            }
            catch
            {
                // Fallback: estimate from sample count (8kHz mono)
                AudioDurationSeconds = samples.Length / 8000.0;
            }

            OnPropertyChanged(nameof(HasAudio));
            OnPropertyChanged(nameof(AudioFileName));

            // Initialize preview player
            _previewPlayer?.Dispose();
            _previewPlayer = new SongPreviewPlayer();
            await _previewPlayer.LoadAsync(audioPath);
            UpdateMetronome();

            // Initialize playhead update timer (60fps)
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
        catch
        {
            WaveformSamples = [];
            AudioDurationSeconds = 0;
        }
    }

    public void Accept()
    {
        StopPreview();

        if (string.IsNullOrWhiteSpace(MapName) ||
            string.IsNullOrWhiteSpace(AudioFilePath) ||
            string.IsNullOrWhiteSpace(OutputFolder))
        {
            Result = null;
            return;
        }

        Result = new NewSongResult
        {
            MapName = MapName.Trim(),
            Title = SongTitle.Trim(),
            Artist = Artist.Trim(),
            CoachCount = CoachCount,
            Difficulty = Difficulty,
            Bpm = Bpm,
            BeatsPerMeasure = BeatsPerMeasure,
            AudioFilePath = AudioFilePath,
            ZeroBeatTimeSeconds = ZeroBeatTimeSeconds,
            StartBeat = StartBeat,
            EndBeat = EndBeat,
            Sections = Sections,
            OutputFolder = OutputFolder
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
