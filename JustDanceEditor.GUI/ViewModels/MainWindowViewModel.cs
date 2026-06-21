using Avalonia.Media.Imaging;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.AppHost;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.GUI.Services;
using JustDanceEditor.GUI.ViewModels.Prompts;

using KevInc.Avalonia.Logging;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;

namespace JustDanceEditor.GUI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string DefaultPlatformCode = "pc";
    private const string DefaultTargetCode = "jdi";
    private readonly IJdiFormat[] _formats;
    private readonly IFormatConversionStrategy[] _strategies;
    private readonly IToolProvider[] _toolProviders;
    private readonly ISongPreviewProvider[] _previewProviders;
    private readonly CoverPreviewBuilder _coverPreviewBuilder;
    private readonly UiLogBuffer _logBuffer;
    private readonly IApplicationDialogService _dialogs;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly List<MenuItemViewModel> _menuRoots = [];

    private ConversionTargetDefinition[] _targets = [];
    private CancellationTokenSource? _previewLoadCts;
    private CancellationTokenSource? _formatDetectionCts;
    private CancellationTokenSource? _outputDetectionCts;
    private CancellationTokenSource? _previewOnlineAssetCts;
    private JdiImportResult? _previewImportResult;
    private string? _previewLoadedFormatName;
    private Task? _previewAssetWarmupTask;
    private string? _previewTempRoot;
    private bool _updatingSongSelection;
    private bool _updatingCoverGenerators;
    private bool _mapBackgroundSourceUserSelected;
    private bool _albumCoachSourceUserSelected;
    private MenuItemViewModel? _activityLogMenuItem;
    private ToolMenuItemViewModel? _selectedTool;
    private bool _isDisposed;

    public MainWindowViewModel(
        IEnumerable<IJdiFormat> formats,
        IEnumerable<IFormatConversionStrategy> strategies,
        IEnumerable<IToolProvider> toolProviders,
        IEnumerable<ISongPreviewProvider> previewProviders,
        UiLogBuffer logBuffer,
        IApplicationDialogService dialogs,
        ILogger<MainWindowViewModel> logger)
    {
        _formats = [.. formats];
        _strategies = [.. strategies];
        _toolProviders = [.. toolProviders];
        _previewProviders = [.. previewProviders.OrderBy(provider => provider.Priority)];
        _logBuffer = logBuffer;
        _dialogs = dialogs;
        _logger = logger;
        _coverPreviewBuilder = new CoverPreviewBuilder(_logger);

        BuildMenus();
        LoadTargets();
        ClearPreview("Select a source to load the song.");
        ShowPage(GuiPage.Source);
        _ = UpdateDetectedFormatAsync();
    }

    public ObservableCollection<MenuItemViewModel> MainMenu { get; } = [];

    public ObservableCollection<PlatformItemViewModel> Platforms { get; } = [];

    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<SongItemViewModel> Songs { get; } = [];

    public ObservableCollection<PromptInputViewModel> TargetPrompts { get; } = [];

    public ObservableCollection<PromptInputViewModel> ToolPrompts { get; } = [];

    public ObservableCollection<CoverGeneratorItemViewModel> SquareCoverGenerators { get; } = [];

    public ObservableCollection<CoverGeneratorItemViewModel> WideCoverGenerators { get; } = [];

    public ObservableCollection<CoverAssetSourceItemViewModel> MapBackgroundSources { get; } = [];

    public ObservableCollection<CoverAssetSourceItemViewModel> AlbumCoachSources { get; } = [];

    public ObservableCollection<string> LogEntries => _logBuffer.Entries;

    public bool IsSourcePage => CurrentPage == GuiPage.Source;

    public bool IsTargetPage => CurrentPage == GuiPage.Target;

    public bool IsOutputPage => CurrentPage == GuiPage.Output;

    public bool IsToolPage => CurrentPage == GuiPage.Tool;

    public bool IsBackVisible => CurrentPage != GuiPage.Source;

    public bool IsPreviewVisible => CurrentPage != GuiPage.Tool;

    public bool IsStepPanelVisible => CurrentPage != GuiPage.Tool;

    public int WorkflowColumnSpan => CurrentPage == GuiPage.Tool ? 2 : 1;

    public bool CanUseActions => !IsBusy;

    [ObservableProperty]
    public partial GuiPage CurrentPage { get; set; }

    [ObservableProperty]
    public partial string PageTitle { get; set; } = "Select Song";

    [ObservableProperty]
    public partial string PrimaryActionText { get; set; } = "Next";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready.";

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
    public partial PlatformItemViewModel? SelectedPlatform { get; set; }

    [ObservableProperty]
    public partial TargetItemViewModel? SelectedTarget { get; set; }

    [ObservableProperty]
    public partial CoverGeneratorItemViewModel? SelectedSquareCoverGenerator { get; set; }

    [ObservableProperty]
    public partial CoverGeneratorItemViewModel? SelectedWideCoverGenerator { get; set; }

    [ObservableProperty]
    public partial CoverAssetSourceItemViewModel? SelectedMapBackgroundSource { get; set; }

    [ObservableProperty]
    public partial CoverAssetSourceItemViewModel? SelectedAlbumCoachSource { get; set; }

    [ObservableProperty]
    public partial bool IsSquareCoverGeneratorVisible { get; set; }

    [ObservableProperty]
    public partial bool IsWideCoverGeneratorVisible { get; set; }

    [ObservableProperty]
    public partial bool AreCoverGeneratorOptionsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsCompositionSourceOptionsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsMapBackgroundSourceOptionsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAlbumCoachSourceOptionsVisible { get; set; }

    [ObservableProperty]
    public partial string TargetSupportText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SongItemViewModel? SelectedSong { get; set; }

    [ObservableProperty]
    public partial bool IsSongSelectionVisible { get; set; }

    [ObservableProperty]
    public partial Bitmap? CoverPreviewImage { get; set; }

    [ObservableProperty]
    public partial Bitmap? CoverPreviewSecondaryImage { get; set; }

    [ObservableProperty]
    public partial int CoverPreviewWidth { get; set; } = 512;

    [ObservableProperty]
    public partial int CoverPreviewHeight { get; set; } = 512;

    [ObservableProperty]
    public partial int CoverPreviewSecondaryWidth { get; set; } = 640;

    [ObservableProperty]
    public partial int CoverPreviewSecondaryHeight { get; set; } = 360;

    [ObservableProperty]
    public partial bool IsSingleCoverPreviewVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDualCoverPreviewVisible { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewPlaceholderVisible { get; set; } = true;

    [ObservableProperty]
    public partial string PreviewPlaceholderText { get; set; } = "Select a source to load the song.";

    [ObservableProperty]
    public partial string PreviewTitleText { get; set; } = "No song loaded";

    [ObservableProperty]
    public partial string PreviewArtistText { get; set; } = "-";

    [ObservableProperty]
    public partial string PreviewMapText { get; set; } = "Map: -";

    [ObservableProperty]
    public partial string PreviewFormatText { get; set; } = "Format: -";

    [ObservableProperty]
    public partial string PreviewStatusText { get; set; } = "Waiting";

    [ObservableProperty]
    public partial string ToolTitleText { get; set; } = "Tool";

    [ObservableProperty]
    public partial string ToolDescriptionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLogDrawerVisible { get; set; }

    partial void OnCurrentPageChanged(GuiPage value)
    {
        OnPropertyChanged(nameof(IsSourcePage));
        OnPropertyChanged(nameof(IsTargetPage));
        OnPropertyChanged(nameof(IsOutputPage));
        OnPropertyChanged(nameof(IsToolPage));
        OnPropertyChanged(nameof(IsBackVisible));
        OnPropertyChanged(nameof(IsPreviewVisible));
        OnPropertyChanged(nameof(IsStepPanelVisible));
        OnPropertyChanged(nameof(WorkflowColumnSpan));

        PageTitle = value switch
        {
            GuiPage.Target => "Target and Options",
            GuiPage.Tool => "Tool",
            _ => "Select Folders"
        };

        PrimaryActionText = value switch
        {
            GuiPage.Target => "Convert",
            GuiPage.Tool => "Run Tool",
            _ => "Next"
        };
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseActions));
    }

    partial void OnCoverPreviewImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    partial void OnCoverPreviewSecondaryImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    partial void OnInputPathChanged(string value)
    {
        _ = UpdateInputPathAsync(resetSongSelection: true);
    }

    partial void OnOutputPathChanged(string value)
    {
        _ = UpdateDetectedOutputAsync();
    }

    partial void OnDownloadAssetsWhenLoadingChanged(bool value)
    {
        if (value)
            StartPreviewOnlineAssetRequest();
        else
            CancelAndDispose(ref _previewOnlineAssetCts);

        RebuildCoverGeneratorOptions(SelectedTarget?.Target);
        _ = RefreshCurrentPreviewAssetAsync();
    }

    partial void OnSelectedPlatformChanged(PlatformItemViewModel? value)
    {
        LoadTargetsForPlatform(value);
    }

    partial void OnSelectedTargetChanged(TargetItemViewModel? value)
    {
        _ = UpdateSelectedTargetAsync(value);
    }

    partial void OnSelectedSquareCoverGeneratorChanged(CoverGeneratorItemViewModel? value)
    {
        UpdateCompositionSourceOptionsVisibility();
        if (!_updatingCoverGenerators)
            _ = RefreshCurrentPreviewAssetAsync();
    }

    partial void OnSelectedWideCoverGeneratorChanged(CoverGeneratorItemViewModel? value)
    {
        UpdateCompositionSourceOptionsVisibility();
        if (!_updatingCoverGenerators)
            _ = RefreshCurrentPreviewAssetAsync();
    }

    partial void OnSelectedMapBackgroundSourceChanged(CoverAssetSourceItemViewModel? value)
    {
        if (!_updatingCoverGenerators && value is not null)
            _mapBackgroundSourceUserSelected = true;

        if (!_updatingCoverGenerators)
            _ = RefreshCurrentPreviewAssetAsync();
    }

    partial void OnSelectedAlbumCoachSourceChanged(CoverAssetSourceItemViewModel? value)
    {
        if (!_updatingCoverGenerators && value is not null)
            _albumCoachSourceUserSelected = true;

        if (!_updatingCoverGenerators)
            _ = RefreshCurrentPreviewAssetAsync();
    }

    partial void OnSelectedSongChanged(SongItemViewModel? value)
    {
        if (_updatingSongSelection)
            return;

        _ = LoadPreviewAsync(resetSongSelection: false);
    }

    partial void OnIsLogDrawerVisibleChanged(bool value)
    {
        if (_activityLogMenuItem is not null)
            _activityLogMenuItem.Header = value ? "Hide Activity Log" : "Activity Log";
    }

    [RelayCommand]
    private void Exit()
    {
        _dialogs.CloseMainWindow();
    }

    [RelayCommand]
    private void ToggleActivityLog()
    {
        IsLogDrawerVisible = !IsLogDrawerVisible;
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logBuffer.Entries.Clear();
    }

    [RelayCommand]
    private async Task BrowseInputFileAsync()
    {
        string? path = await _dialogs.PickFileAsync("Select source file");
        if (path is not null)
            InputPath = path;
    }

    [RelayCommand]
    private async Task BrowseInputFolderAsync()
    {
        string? path = await _dialogs.PickFolderAsync("Select source folder");
        if (path is not null)
            InputPath = path;
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        string? path = await _dialogs.PickFolderAsync("Select output folder");
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

    [RelayCommand]
    private void ShowSourcePage() => ShowPage(GuiPage.Source);

    [RelayCommand]
    private void ShowTargetPage() => ShowPage(GuiPage.Target);

    [RelayCommand]
    private void Back()
    {
        ShowPage(CurrentPage switch
        {
            GuiPage.Target => GuiPage.Source,
            GuiPage.Tool => GuiPage.Source,
            _ => GuiPage.Source
        });
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (IsBusy)
            return;

        switch (CurrentPage)
        {
            case GuiPage.Source:
                await ContinueFromSourceAsync();
                break;
            case GuiPage.Target:
                await RunBusyAsync("Converting...", ConvertAsync);
                break;
            case GuiPage.Tool:
                await RunBusyAsync("Running tool...", RunToolAsync);
                break;
        }
    }

    [RelayCommand]
    private void OpenTool(ToolMenuItemViewModel? selection)
    {
        if (selection is null)
            return;

        _selectedTool = selection;
        ToolTitleText = $"{selection.Provider.ProviderName} - {selection.Tool.DisplayName}";
        ToolDescriptionText = selection.Tool.Description;
        RebuildPrompts(ToolPrompts, selection.Tool.Prompts, skipOutputPath: false);
        ShowPage(GuiPage.Tool);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CancelAndDispose(ref _previewLoadCts);
        CancelAndDispose(ref _formatDetectionCts);
        CancelAndDispose(ref _outputDetectionCts);
        CancelAndDispose(ref _previewOnlineAssetCts);
        CoverPreviewImage = null;
        CoverPreviewSecondaryImage = null;
        CleanupPreviewTemp();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        CancellationTokenSource? current = source;
        source = null;
        if (current is null)
            return;

        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        current.Dispose();
    }

    private void BuildMenus()
    {
        MainMenu.Clear();
        _menuRoots.Clear();

        MenuItemViewModel fileMenu = new() { Header = "_File" };
        fileMenu.Items.Add(new MenuItemViewModel
        {
            Header = "Exit",
            Command = ExitCommand
        });

        MenuItemViewModel toolsMenu = BuildToolsMenu();
        MenuItemViewModel viewMenu = new() { Header = "_View" };
        _activityLogMenuItem = new MenuItemViewModel
        {
            Header = "Activity Log",
            Command = ToggleActivityLogCommand
        };
        viewMenu.Items.Add(_activityLogMenuItem);

        _menuRoots.Add(fileMenu);
        _menuRoots.Add(toolsMenu);
        _menuRoots.Add(viewMenu);

        foreach (MenuItemViewModel item in _menuRoots)
            MainMenu.Add(item);
    }

    private MenuItemViewModel BuildToolsMenu()
    {
        MenuItemViewModel toolsMenu = new() { Header = "_Tools" };

        foreach (IToolProvider provider in _toolProviders
            .Where(provider => provider.GetTools().Count > 0)
            .OrderBy(provider => provider.Priority)
            .ThenBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            ToolDefinition[] tools = [.. provider.GetTools()
                .OrderBy(tool => tool.Priority)
                .ThenBy(tool => tool.DisplayName, StringComparer.OrdinalIgnoreCase)];

            if (tools.Length == 1)
            {
                ToolMenuItemViewModel selection = new(provider, tools[0]);
                toolsMenu.Items.Add(new MenuItemViewModel
                {
                    Header = provider.ProviderName,
                    Command = OpenToolCommand,
                    CommandParameter = selection
                });
                continue;
            }

            MenuItemViewModel providerItem = new() { Header = provider.ProviderName };
            foreach (ToolDefinition tool in tools)
            {
                ToolMenuItemViewModel selection = new(provider, tool);
                providerItem.Items.Add(new MenuItemViewModel
                {
                    Header = tool.DisplayName,
                    Command = OpenToolCommand,
                    CommandParameter = selection
                });
            }

            toolsMenu.Items.Add(providerItem);
        }

        if (toolsMenu.Items.Count == 0)
        {
            toolsMenu.Items.Add(new MenuItemViewModel
            {
                Header = "No tools available",
                IsEnabled = false
            });
        }

        return toolsMenu;
    }

    private void LoadTargets()
    {
        _targets = ConversionTargetSelector.GetAvailableTargets(_strategies);
        Platforms.Clear();

        foreach (PlatformItemViewModel platform in ConversionTargetSelector.GetSortedPlatforms(_targets)
            .Select(platform => new PlatformItemViewModel(platform)))
        {
            Platforms.Add(platform);
        }

        SelectedPlatform = Platforms.FirstOrDefault(platform =>
                platform.Platform.PlatformCode.Equals(DefaultPlatformCode, StringComparison.OrdinalIgnoreCase))
            ?? Platforms.FirstOrDefault();
    }

    private void LoadTargetsForPlatform(PlatformItemViewModel? platform)
    {
        Targets.Clear();
        if (platform is null)
        {
            SelectedTarget = null;
            return;
        }

        foreach (TargetItemViewModel target in ConversionTargetSelector
            .GetSortedTargetsForPlatform(_targets, platform.Platform.PlatformCode)
            .Select(target => new TargetItemViewModel(target)))
        {
            Targets.Add(target);
        }

        SelectedTarget = Targets.FirstOrDefault(target =>
                target.Target.TargetCode.Equals(DefaultTargetCode, StringComparison.OrdinalIgnoreCase)
                || target.Target.FormatCode.Equals(DefaultTargetCode, StringComparison.OrdinalIgnoreCase))
            ?? Targets.FirstOrDefault();
    }

    private async Task UpdateSelectedTargetAsync(TargetItemViewModel? target)
    {
        TargetSupportText = target is null ? string.Empty : GetSupportText(target.Target);
        RebuildPrompts(TargetPrompts, target?.Target.ExportPrompts ?? [], skipOutputPath: true);
        RebuildCoverGeneratorOptions(target?.Target);
        await RefreshCurrentPreviewAssetAsync();
    }

    private void RebuildCoverGeneratorOptions(ConversionTargetDefinition? target)
    {
        _updatingCoverGenerators = true;
        try
        {
            CoverGeneratorOptionResult options = CoverGeneratorOptionService.Build(new CoverGeneratorOptionRequest(
                Target: target,
                SourceFormatName: _previewLoadedFormatName ?? _previewImportResult?.SourceFormat,
                Inventory: new CoverAssetInventory(_previewImportResult?.MaterializedRoot, DownloadAssetsWhenLoading),
                PreviousSquareGenerator: SelectedSquareCoverGenerator?.Kind,
                PreviousWideGenerator: SelectedWideCoverGenerator?.Kind,
                PreviousMapBackgroundSource: SelectedMapBackgroundSource?.Kind,
                PreviousAlbumCoachSource: SelectedAlbumCoachSource?.Kind,
                PreserveMapBackgroundSource: _mapBackgroundSourceUserSelected,
                PreserveAlbumCoachSource: _albumCoachSourceUserSelected));

            ReplaceCollection(SquareCoverGenerators, options.SquareGenerators);
            ReplaceCollection(WideCoverGenerators, options.WideGenerators);
            ReplaceCollection(MapBackgroundSources, options.MapBackgroundSources);
            ReplaceCollection(AlbumCoachSources, options.AlbumCoachSources);

            SelectedSquareCoverGenerator = options.SelectedSquareGenerator;
            SelectedWideCoverGenerator = options.SelectedWideGenerator;
            SelectedMapBackgroundSource = options.SelectedMapBackgroundSource;
            SelectedAlbumCoachSource = options.SelectedAlbumCoachSource;
            IsSquareCoverGeneratorVisible = options.IsSquareCoverGeneratorVisible;
            IsWideCoverGeneratorVisible = options.IsWideCoverGeneratorVisible;
            AreCoverGeneratorOptionsVisible = options.AreCoverGeneratorOptionsVisible;
            UpdateCompositionSourceOptionsVisibility();
        }
        finally
        {
            _updatingCoverGenerators = false;
        }
    }

    private void UpdateCompositionSourceOptionsVisibility()
    {
        bool compositionGeneratorSelected =
            AreCoverGeneratorOptionsVisible &&
            ((SelectedSquareCoverGenerator?.Kind == CoverGeneratorKind.FromMapBackground) ||
             (SelectedWideCoverGenerator?.Kind == CoverGeneratorKind.FromMapBackground));

        IsMapBackgroundSourceOptionsVisible = compositionGeneratorSelected && MapBackgroundSources.Count > 1;
        IsAlbumCoachSourceOptionsVisible = compositionGeneratorSelected && AlbumCoachSources.Count > 1;
        IsCompositionSourceOptionsVisible = IsMapBackgroundSourceOptionsVisible || IsAlbumCoachSourceOptionsVisible;
    }

    private void RebuildPrompts(
        ObservableCollection<PromptInputViewModel> target,
        IReadOnlyList<ConversionPrompt> prompts,
        bool skipOutputPath)
    {
        target.Clear();
        foreach (ConversionPrompt prompt in prompts)
        {
            if (skipOutputPath && prompt.Id.Equals(ConversionPromptIds.OutputPath, StringComparison.OrdinalIgnoreCase))
                continue;

            target.Add(PromptInputViewModel.Create(prompt, _dialogs));
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
            target.Add(item);
    }

    private void ShowPage(GuiPage page)
    {
        CurrentPage = page;
    }

    private async Task UpdateInputPathAsync(bool resetSongSelection)
    {
        _ = UpdateDetectedFormatAsync();
        await LoadPreviewAsync(resetSongSelection);
    }

    private async Task ContinueFromSourceAsync()
    {
        RequireInputPath();
        RequireOutputPath();
        await UpdateDetectedOutputAsync();
        ShowPage(GuiPage.Target);
    }

    private async Task UpdateDetectedFormatAsync()
    {
        string path = NormalizePath(InputPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            DetectedFormatText = "No source selected.";
            return;
        }

        CancelAndDispose(ref _formatDetectionCts);
        CancellationTokenSource cts = new();
        _formatDetectionCts = cts;
        CancellationToken cancellationToken = cts.Token;

        DetectedFormatText = "Detecting source format...";

        try
        {
            string[] detected = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _formats
                    .Where(format => format.CanImport && format.Check(path))
                    .Select(format => format.DisplayName)
                    .ToArray();
            }, cancellationToken);

            if (_formatDetectionCts != cts || cancellationToken.IsCancellationRequested)
                return;

            DetectedFormatText = detected.Length switch
            {
                0 => "Source format: not detected yet.",
                1 => $"Source format: {detected[0]}",
                _ => $"Source formats: {string.Join(", ", detected)}"
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DetectedFormatText = $"Source detection failed: {ex.Message}";
        }
    }

    private async Task UpdateDetectedOutputAsync()
    {
        string path = NormalizePath(OutputPath);
        CancelAndDispose(ref _outputDetectionCts);

        if (string.IsNullOrWhiteSpace(path))
        {
            DetectedOutputText = "No output selected.";
            return;
        }

        CancellationTokenSource cts = new();
        _outputDetectionCts = cts;
        CancellationToken cancellationToken = cts.Token;

        DetectedOutputText = "Detecting output target...";

        try
        {
            OutputTargetDetectionResult result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return OutputTargetDetector.Detect(path, _formats, _targets);
            }, cancellationToken);

            if (_outputDetectionCts != cts || cancellationToken.IsCancellationRequested)
                return;

            DetectedOutputText = result.Message;
            if (result.Target is not null)
                SelectTarget(result.Target);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DetectedOutputText = $"Output detection failed: {ex.Message}";
        }
    }

    private void SelectTarget(ConversionTargetDefinition target)
    {
        PlatformItemViewModel? platform = Platforms.FirstOrDefault(item =>
            item.Platform.PlatformCode.Equals(target.Platform.PlatformCode, StringComparison.OrdinalIgnoreCase));
        if (platform is null)
            return;

        if (!ReferenceEquals(SelectedPlatform, platform))
            SelectedPlatform = platform;

        TargetItemViewModel? targetItem = Targets.FirstOrDefault(item =>
            item.Target.TargetCode.Equals(target.TargetCode, StringComparison.OrdinalIgnoreCase) &&
            item.Target.FormatCode.Equals(target.FormatCode, StringComparison.OrdinalIgnoreCase));
        if (targetItem is not null)
            SelectedTarget = targetItem;
    }

    private async Task LoadPreviewAsync(bool resetSongSelection)
    {
        string inputPath = NormalizePath(InputPath);
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            ClearPreview("Select a source to load the song.");
            StatusText = "Ready.";
            return;
        }

        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            ClearPreview("Source path not found.");
            StatusText = "Source path not found.";
            return;
        }

        CancelAndDispose(ref _previewLoadCts);
        CancelAndDispose(ref _previewOnlineAssetCts);
        CancellationTokenSource cts = new();
        _previewLoadCts = cts;
        CancellationToken cancellationToken = cts.Token;

        if (resetSongSelection)
            ClearSongChoices();

        SetBusy(true, "Loading song...");
        ClearPreview("Loading song...");

        try
        {
            PreviewImport preview = await ImportPreviewPackageAsync(inputPath, cancellationToken);
            if (_previewLoadCts != cts)
                return;

            _previewImportResult = preview.ImportResult;
            _previewLoadedFormatName = preview.FormatName;
            _previewAssetWarmupTask = preview.AssetWarmupTask;

            RebuildCoverGeneratorOptions(SelectedTarget?.Target);
            Task onlineAssetTask = DownloadOnlineAssetsForPreviewAsync(preview.ImportResult, SelectedTarget?.Target, cancellationToken);
            await WaitForPreviewAssetWarmupIfNeededAsync(preview.AssetWarmupTask, SelectedTarget?.Target, preview.FormatName, cancellationToken);
            await onlineAssetTask;
            RebuildCoverGeneratorOptions(SelectedTarget?.Target);
            await DisplayPreviewAsync(preview.ImportResult, SelectedTarget?.Target, preview.FormatName, cancellationToken);
            StatusText = "Song loaded.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (MultipleConversionItemsFoundException ex)
        {
            ShowSongChoices(ex.AvailableItems);
            ClearPreview("Choose a song from the source.");
            StatusText = "Choose a song from the source.";
            _logger.LogInformation("Source contains multiple songs: {Songs}", string.Join(", ", ex.AvailableItems));
        }
        catch (Exception ex)
        {
            ClearPreview("Could not load this source.");
            StatusText = ex.Message;
            _logger.LogError(ex, "Failed to load source preview: {Message}", ex.Message);
        }
        finally
        {
            if (_previewLoadCts == cts)
                SetBusy(false);
        }
    }

    private async Task<PreviewImport> ImportPreviewPackageAsync(string inputPath, CancellationToken cancellationToken)
    {
        CleanupPreviewTemp();
        _previewTempRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "GuiPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_previewTempRoot);

        string? selectedSong = GetSongName();
        bool downloadOnlineAssets = DownloadAssetsWhenLoading;
        ISongPreviewProvider? previewProvider = await Task.Run(
            () => _previewProviders.FirstOrDefault(provider => provider.CanPreview(inputPath)),
            cancellationToken);

        if (previewProvider is not null)
        {
            bool usePreviewWorkingCopy = downloadOnlineAssets ||
                previewProvider.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase);

            SongPreviewResult previewResult = await Task.Run(async () => await previewProvider.LoadPreviewAsync(new SongPreviewRequest(
                inputPath,
                _previewTempRoot,
                selectedSong,
                usePreviewWorkingCopy), cancellationToken), cancellationToken);

            JdiImportResult previewImportResult = new(
                previewResult.Package,
                previewResult.FormatName,
                previewResult.MaterializedRoot,
                previewResult.MaterializedRootIsTemporary);

            return new PreviewImport(previewImportResult, previewResult.FormatName, previewResult.AssetWarmupTask);
        }

        IJdiFormat sourceFormat = ResolveSourceFormat(inputPath);
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(sourceFormat.DisplayName);

        string previewInputPath = inputPath;
        string intermediatePath = Path.Combine(_previewTempRoot, "jdi");
        bool copyJdiForPreview = sourceFormat.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(inputPath);

        if (copyJdiForPreview)
        {
            previewInputPath = Path.Combine(_previewTempRoot, "source");
            CopyDirectory(inputPath, previewInputPath);
            intermediatePath = previewInputPath;
        }
        else if (sourceFormat.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase) && Directory.Exists(inputPath))
        {
            intermediatePath = inputPath;
        }

        PromptAnswerSet answers = BuildPreviewAnswers(_previewTempRoot);
        IConversionInteraction interaction = new StaticConversionInteraction(answers);
        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(
            previewInputPath,
            intermediatePath,
            selectedSong,
            Interaction: interaction));

        JdiImportResult importResult = await Task.Run(async () => await sourceFormat.ImportAsync(importRequest, cancellationToken), cancellationToken);
        return new PreviewImport(importResult, sourceFormat.DisplayName, null);
    }

    private async Task RefreshCurrentPreviewAssetAsync(bool rebuildGeneratorOptions = false)
    {
        if (_previewImportResult?.MaterializedRoot is null)
            return;

        try
        {
            await WaitForPreviewAssetWarmupIfNeededAsync(_previewAssetWarmupTask, SelectedTarget?.Target, _previewLoadedFormatName ?? _previewImportResult.SourceFormat, CancellationToken.None);
            if (rebuildGeneratorOptions)
                RebuildCoverGeneratorOptions(SelectedTarget?.Target);
            await DisplayPreviewAsync(_previewImportResult, SelectedTarget?.Target, _previewLoadedFormatName ?? "JDI", CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            _logger.LogWarning(ex, "Failed to refresh preview asset for selected target.");
        }
    }

    private async Task DisplayPreviewAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition? target,
        string sourceFormatName,
        CancellationToken cancellationToken)
    {
        CoverPreviewData preview = await _coverPreviewBuilder.BuildPreviewDataAsync(
            importResult,
            target,
            CreateCoverPreviewOptions(),
            cancellationToken);

        CoverPreviewImage = preview.Bitmap;
        CoverPreviewSecondaryImage = preview.SecondaryBitmap;
        CoverPreviewWidth = preview.Width;
        CoverPreviewHeight = preview.Height;
        CoverPreviewSecondaryWidth = preview.SecondaryWidth;
        CoverPreviewSecondaryHeight = preview.SecondaryHeight;
        IsDualCoverPreviewVisible = preview.SecondaryBitmap is not null;
        IsSingleCoverPreviewVisible = !IsDualCoverPreviewVisible;
        IsPreviewPlaceholderVisible = false;
        PreviewTitleText = string.IsNullOrWhiteSpace(preview.Package.Metadata.Title)
            ? preview.Package.Metadata.MapName
            : preview.Package.Metadata.Title;
        PreviewArtistText = string.IsNullOrWhiteSpace(preview.Package.Metadata.Artist) ? "-" : preview.Package.Metadata.Artist;
        PreviewMapText = $"Map: {preview.Package.Metadata.MapName}";
        PreviewFormatText = $"Format: {sourceFormatName}";
        PreviewStatusText = preview.Package.Metadata.OriginalJDVersion > 0
            ? $"JD{preview.Package.Metadata.OriginalJDVersion}"
            : "Loaded";
    }

    private async Task ConvertAsync(CancellationToken cancellationToken)
    {
        string inputPath = RequireInputPath();
        string outputPath = RequireOutputPath();
        string? songName = GetSongName();
        ConversionTargetDefinition target = SelectedTarget?.Target ?? throw new InvalidOperationException("Select a target first.");
        IJdiFormat sourceFormat = ResolveSourceFormat(inputPath);
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(sourceFormat.DisplayName);
        IFormatConversionStrategy targetStrategy = ResolveStrategy(target.FormatName);
        IJdiFormat targetFormat = ResolveFormat(target.FormatName);
        bool downloadOnlineAssets = DownloadAssetsWhenLoading;

        await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);

        PromptAnswerSet answers = BuildConversionAnswers();
        IConversionInteraction interaction = new GuiConversionInteraction(_dialogs, answers);
        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileNameWithoutExtension(inputPath) ?? "Export");

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(inputPath, intermediatePath, songName, Interaction: interaction));
        ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(new ConversionRequestContext(inputPath, outputPath, songName, target, answers, interaction));

        JdiImportResult importResult = await Task.Run(async () => await sourceFormat.ImportAsync(importRequest, cancellationToken), cancellationToken);
        try
        {
            if (downloadOnlineAssets && importResult.MaterializedRoot is not null)
                await DownloadOnlineAssetsForPreviewAsync(importResult, target, cancellationToken);

            await ApplySelectedCoverGeneratorsAsync(importResult, target, cancellationToken);

            if (importResult.MaterializedRoot is not null)
                await DisplayPreviewAsync(importResult, target, sourceFormat.DisplayName, cancellationToken);

            await Task.Run(async () => await targetFormat.ExportAsync(importResult, exportRequest, cancellationToken), cancellationToken);
        }
        finally
        {
            CleanupTemporaryImport(importResult);
        }
    }

    private async Task RunToolAsync(CancellationToken cancellationToken)
    {
        ToolMenuItemViewModel selection = _selectedTool ?? throw new InvalidOperationException("Select a tool from the Tools menu first.");
        PromptAnswerSet answers = BuildAnswersFromInputs(ToolPrompts);
        IConversionInteraction interaction = new GuiConversionInteraction(_dialogs, answers);
        await Task.Run(async () => await selection.Provider.ExecuteAsync(new ToolExecutionContext(selection.Tool, answers, interaction), cancellationToken), cancellationToken);
    }

    private PromptAnswerSet BuildPreviewAnswers(string previewRoot)
    {
        PromptAnswerSet answers = new();
        answers.Set(ConversionPromptIds.OutputPath, previewRoot);

        string? songName = GetSongName();
        if (!string.IsNullOrWhiteSpace(songName))
            answers.Set("ubiart.songName", songName);

        return answers;
    }

    private void StartPreviewOnlineAssetRequest()
    {
        CancelAndDispose(ref _previewOnlineAssetCts);

        if (!DownloadAssetsWhenLoading || _previewImportResult?.MaterializedRoot is null)
            return;

        JdiImportResult importResult = _previewImportResult;
        CancellationTokenSource cts = new();
        _previewOnlineAssetCts = cts;
        CancellationToken cancellationToken = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await DownloadOnlineAssetsForPreviewAsync(importResult, SelectedTarget?.Target, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                Dispatcher.UIThread.Post(async () =>
                {
                    if (_isDisposed ||
                        !ReferenceEquals(_previewOnlineAssetCts, cts) ||
                        !ReferenceEquals(_previewImportResult, importResult))
                    {
                        return;
                    }

                    RebuildCoverGeneratorOptions(SelectedTarget?.Target);
                    await RefreshCurrentPreviewAssetAsync();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Online preview asset request failed.");
            }
        }, CancellationToken.None);
    }

    private async Task DownloadOnlineAssetsForPreviewAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition? target,
        CancellationToken cancellationToken)
    {
        if (!DownloadAssetsWhenLoading || importResult.MaterializedRoot is null)
            return;

        OnlineAssetDefinition[] requestedAssets =
        [
            .. CoverRules.GetRequiredOnlineAssetsForSelection(
                target,
                ResolveSquareCoverGeneratorKind(),
                ResolveWideCoverGeneratorKind())
        ];
        if (requestedAssets.Length == 0)
            return;

        await new OnlineAssetDownloader(_logger).DownloadAssetsAsync(
            importResult.MaterializedRoot,
            importResult.Package,
            requestedAssets,
            mapRelativeAssetPath: CoverRules.GetWebAssetRelativePath,
            cancellationToken: cancellationToken);
    }

    private PromptAnswerSet BuildConversionAnswers()
    {
        PromptAnswerSet answers = new();
        answers.Set(ConversionPromptIds.OutputPath, RequireOutputPath());

        foreach (KeyValuePair<string, string> answer in BuildAnswersFromInputs(TargetPrompts).Answers)
            answers.Set(answer.Key, answer.Value);

        string? songName = GetSongName();
        if (!string.IsNullOrWhiteSpace(songName))
            answers.Set("ubiart.songName", songName);

        return answers;
    }

    private static PromptAnswerSet BuildAnswersFromInputs(IEnumerable<PromptInputViewModel> inputs)
    {
        PromptAnswerSet answers = new();
        foreach (PromptInputViewModel input in inputs)
        {
            if (input.ShouldInclude)
                answers.Set(input.Id, input.Value);
        }

        return answers;
    }

    private async Task RunBusyAsync(string busyText, Func<CancellationToken, Task> operation)
    {
        SetBusy(true, busyText);
        try
        {
            await operation(CancellationToken.None);
            StatusText = "Done.";
        }
        catch (MultipleConversionItemsFoundException ex)
        {
            ShowSongChoices(ex.AvailableItems);
            ShowPage(GuiPage.Source);
            StatusText = "Choose a song from the source.";
            _logger.LogWarning("Multiple maps found: {Maps}", string.Join(", ", ex.AvailableItems));
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            _logger.LogError(ex, "GUI operation failed: {Message}", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        IsBusy = busy;
        if (status is not null)
            StatusText = status;
    }

    private void ClearSongChoices()
    {
        _updatingSongSelection = true;
        Songs.Clear();
        SelectedSong = null;
        IsSongSelectionVisible = false;
        _updatingSongSelection = false;
    }

    private void ShowSongChoices(IEnumerable<string> songs)
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

    private void ClearPreview(string message)
    {
        CoverPreviewImage = null;
        CoverPreviewSecondaryImage = null;
        CoverPreviewWidth = 512;
        CoverPreviewHeight = 512;
        CoverPreviewSecondaryWidth = 640;
        CoverPreviewSecondaryHeight = 360;
        IsSingleCoverPreviewVisible = false;
        IsDualCoverPreviewVisible = false;
        PreviewPlaceholderText = message;
        IsPreviewPlaceholderVisible = true;
        PreviewTitleText = "No song loaded";
        PreviewArtistText = "-";
        PreviewMapText = "Map: -";
        PreviewFormatText = "Format: -";
        PreviewStatusText = "Waiting";
        _previewImportResult = null;
        _previewLoadedFormatName = null;
        _previewAssetWarmupTask = null;
        _mapBackgroundSourceUserSelected = false;
        _albumCoachSourceUserSelected = false;
        RebuildCoverGeneratorOptions(SelectedTarget?.Target);
    }

    private string RequireInputPath()
    {
        string path = NormalizePath(InputPath);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose a source path first.");
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Source path not found.", path);
        return path;
    }

    private string RequireOutputPath()
    {
        string path = NormalizePath(OutputPath);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose an output folder first.");
        return path;
    }

    private string? GetSongName()
    {
        return IsSongSelectionVisible && SelectedSong is not null ? SelectedSong.Name : null;
    }

    private IJdiFormat ResolveSourceFormat(string inputPath)
    {
        IJdiFormat[] detected = [.. _formats.Where(format => format.CanImport && format.Check(inputPath))];
        return detected.Length switch
        {
            1 => detected[0],
            > 1 => detected.FirstOrDefault(format => !format.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase)) ?? detected[0],
            _ => throw new InvalidOperationException($"Could not detect source format for '{inputPath}'.")
        };
    }

    private IJdiFormat ResolveFormat(string format)
    {
        IFormatConversionStrategy? strategy = _strategies.FirstOrDefault(strategy =>
            strategy.FormatCode.Equals(format, StringComparison.OrdinalIgnoreCase) ||
            strategy.FormatName.Equals(format, StringComparison.OrdinalIgnoreCase));

        string formatName = strategy?.FormatName ?? format;
        return _formats.FirstOrDefault(item => item.DisplayName.Equals(formatName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown format '{format}'.");
    }

    private IFormatConversionStrategy ResolveStrategy(string format)
    {
        return _strategies.FirstOrDefault(strategy =>
                strategy.FormatCode.Equals(format, StringComparison.OrdinalIgnoreCase) ||
                strategy.FormatName.Equals(format, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No conversion strategy registered for '{format}'.");
    }

    private CoverGeneratorKind ResolveSquareCoverGeneratorKind() =>
        SelectedSquareCoverGenerator?.Kind ?? CoverGeneratorKind.Automatic;

    private CoverGeneratorKind ResolveWideCoverGeneratorKind() =>
        SelectedWideCoverGenerator?.Kind ?? CoverGeneratorKind.Automatic;

    private CoverPreviewOptions CreateCoverPreviewOptions() =>
        new(
            DownloadAssetsWhenLoading,
            ResolveSquareCoverGeneratorKind(),
            ResolveWideCoverGeneratorKind(),
            SelectedMapBackgroundSource?.Kind,
            SelectedAlbumCoachSource?.Kind);

    private async Task WaitForPreviewAssetWarmupIfNeededAsync(
        Task? warmupTask,
        ConversionTargetDefinition? target,
        string? sourceFormatName,
        CancellationToken cancellationToken)
    {
        if (warmupTask is null ||
            warmupTask.IsCompleted ||
            !CoverRules.SelectedCoverGeneratorsNeedMapBackground(
                target,
                sourceFormatName,
                ResolveSquareCoverGeneratorKind(),
                ResolveWideCoverGeneratorKind()))
            return;

        Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        Task completedTask = await Task.WhenAny(warmupTask, timeoutTask);
        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask != warmupTask)
        {
            SchedulePreviewRefreshAfterWarmup(warmupTask);
            return;
        }

        try
        {
            await warmupTask;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Preview asset warmup failed.");
        }
    }

    private void SchedulePreviewRefreshAfterWarmup(Task warmupTask)
    {
        _ = warmupTask.ContinueWith(task =>
        {
            if (task.IsFaulted)
                _logger.LogDebug(task.Exception, "Preview asset warmup failed.");
            if (task.IsCanceled || task.IsFaulted)
                return;

            Dispatcher.UIThread.Post(async () =>
            {
                if (_isDisposed || !ReferenceEquals(_previewAssetWarmupTask, warmupTask))
                    return;

                await RefreshCurrentPreviewAssetAsync(rebuildGeneratorOptions: true);
            });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ApplySelectedCoverGeneratorsAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition target,
        CancellationToken cancellationToken)
    {
        await _coverPreviewBuilder.ApplySelectedCoverGeneratorsAsync(
            importResult,
            target,
            CreateCoverPreviewOptions(),
            cancellationToken);
    }

    private static string GetSupportText(ConversionTargetDefinition target) => target.SupportStatus switch
    {
        ConversionSupportStatus.Experimental => "Experimental target. Import/export may need extra verification.",
        ConversionSupportStatus.KnownPartial => "Known partial support. Basic conversion works, but platform-native dumps are still limited.",
        _ => string.Empty
    };

    private static void CleanupTemporaryImport(JdiImportResult importResult)
    {
        if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
            Directory.Delete(importResult.MaterializedRoot, true);
    }

    private void CleanupPreviewTemp()
    {
        if (_previewTempRoot is not null && Directory.Exists(_previewTempRoot))
        {
            try
            {
                Directory.Delete(_previewTempRoot, true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete preview temp folder {Folder}", _previewTempRoot);
            }
        }

        _previewTempRoot = null;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (string directory in Directory.GetDirectories(sourceDir))
            CopyDirectory(directory, Path.Combine(destDir, Path.GetFileName(directory)));
    }

    private static string NormalizePath(string path) => path.Trim().Trim('"');

    private sealed record PreviewImport(JdiImportResult ImportResult, string FormatName, Task? AssetWarmupTask);
}
