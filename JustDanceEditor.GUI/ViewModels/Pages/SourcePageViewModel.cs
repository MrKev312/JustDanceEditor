using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.GUI.Services;
using JustDanceEditor.GUI.ViewModels;

using System.Collections.ObjectModel;

namespace JustDanceEditor.GUI.ViewModels.Pages;

public sealed partial class SourcePageViewModel(IApplicationDialogService dialogs) : ViewModelBase
{
    private bool _updatingSongSelection;

    public ObservableCollection<SongItemViewModel> Songs { get; } = [];

    public bool CanContinue
    {
        get
        {
            string inputPath = NormalizedInputPath;
            return !string.IsNullOrWhiteSpace(inputPath) &&
                (File.Exists(inputPath) || Directory.Exists(inputPath)) &&
                !string.IsNullOrWhiteSpace(NormalizedOutputPath);
        }
    }

    public string NormalizedInputPath => NormalizePath(InputPath);

    public string NormalizedOutputPath => NormalizePath(OutputPath);

    [ObservableProperty]
    public partial string InputPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DownloadAssetsWhenLoading { get; set; } = true;

    [ObservableProperty]
    public partial string DetectedFormatText { get; set; } = "No source selected.";

    [ObservableProperty]
    public partial string DetectedOutputText { get; set; } = "No output selected.";

    [ObservableProperty]
    public partial SongItemViewModel? SelectedSong { get; set; }

    [ObservableProperty]
    public partial bool IsSongSelectionVisible { get; set; }

    public event EventHandler? InputPathUpdated;

    public event EventHandler? OutputPathUpdated;

    public event EventHandler? DownloadAssetsWhenLoadingUpdated;

    public event EventHandler? SelectedSongUpdated;

    partial void OnInputPathChanged(string value)
    {
        OnPropertyChanged(nameof(NormalizedInputPath));
        OnPropertyChanged(nameof(CanContinue));
        InputPathUpdated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnOutputPathChanged(string value)
    {
        OnPropertyChanged(nameof(NormalizedOutputPath));
        OnPropertyChanged(nameof(CanContinue));
        OutputPathUpdated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDownloadAssetsWhenLoadingChanged(bool value)
    {
        DownloadAssetsWhenLoadingUpdated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedSongChanged(SongItemViewModel? value)
    {
        if (!_updatingSongSelection)
            SelectedSongUpdated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task BrowseInputFileAsync()
    {
        string? path = await dialogs.PickFileAsync("Select source file");
        if (path is not null)
            InputPath = path;
    }

    [RelayCommand]
    private async Task BrowseInputFolderAsync()
    {
        string? path = await dialogs.PickFolderAsync("Select source folder");
        if (path is not null)
            InputPath = path;
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        string? path = await dialogs.PickFolderAsync("Select output folder");
        if (path is not null)
            OutputPath = path;
    }

    [RelayCommand]
    private void SetInputPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            InputPath = path;
    }

    [RelayCommand]
    private void SetOutputPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            OutputPath = path;
    }

    public void ClearSongChoices()
    {
        _updatingSongSelection = true;
        Songs.Clear();
        SelectedSong = null;
        IsSongSelectionVisible = false;
        _updatingSongSelection = false;
    }

    public void ShowSongChoices(IEnumerable<string> songs)
    {
        SongItemViewModel[] songItems = [.. songs
            .Where(song => !string.IsNullOrWhiteSpace(song))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(song => song, StringComparer.OrdinalIgnoreCase)
            .Select(song => new SongItemViewModel(song))];

        _updatingSongSelection = true;
        Songs.Clear();
        foreach (SongItemViewModel song in songItems)
            Songs.Add(song);
        SelectedSong = songItems.Length == 1 ? songItems[0] : null;
        IsSongSelectionVisible = songItems.Length > 0;
        _updatingSongSelection = false;
    }

    public string RequireInputPath()
    {
        string path = NormalizedInputPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose a source path first.");
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Source path not found.", path);
        return path;
    }

    public string RequireOutputPath()
    {
        string path = NormalizedOutputPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose an output folder first.");
        return path;
    }

    public string? GetSongName() =>
        IsSongSelectionVisible && SelectedSong is not null ? SelectedSong.Name : null;

    private static string NormalizePath(string path) => path.Trim().Trim('"');
}