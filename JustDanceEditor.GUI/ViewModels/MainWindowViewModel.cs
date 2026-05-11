using System.Collections.ObjectModel;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.AppHost;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.GUI.ViewModels.Prompts;

using Microsoft.Extensions.Logging;

using KevInc.Avalonia.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.GUI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IJdiFormat[] _formats;
    private readonly IFormatConversionStrategy[] _strategies;
    private readonly IToolProvider[] _toolProviders;
    private readonly ISongPreviewProvider[] _previewProviders;
    private readonly UiLogBuffer _logBuffer;
    private readonly IApplicationDialogService _dialogs;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly List<MenuItemViewModel> _menuRoots = [];

    private ConversionTargetDefinition[] _targets = [];
    private CancellationTokenSource? _previewLoadCts;
    private CancellationTokenSource? _formatDetectionCts;
    private JdiImportResult? _previewImportResult;
    private string? _previewLoadedFormatName;
    private string? _previewTempRoot;
    private bool _updatingSongSelection;
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
    private GuiPage _currentPage;

    [ObservableProperty]
    private string _pageTitle = "Select Song";

    [ObservableProperty]
    private string _primaryActionText = "Next";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready.";

    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private bool _downloadAssetsWhenLoading;

    [ObservableProperty]
    private string _detectedFormatText = "No source selected.";

    [ObservableProperty]
    private PlatformItemViewModel? _selectedPlatform;

    [ObservableProperty]
    private TargetItemViewModel? _selectedTarget;

    [ObservableProperty]
    private string _targetSupportText = string.Empty;

    [ObservableProperty]
    private SongItemViewModel? _selectedSong;

    [ObservableProperty]
    private bool _isSongSelectionVisible;

    [ObservableProperty]
    private Bitmap? _coverPreviewImage;

    [ObservableProperty]
    private int _coverPreviewWidth = 512;

    [ObservableProperty]
    private int _coverPreviewHeight = 512;

    [ObservableProperty]
    private bool _isPreviewPlaceholderVisible = true;

    [ObservableProperty]
    private string _previewPlaceholderText = "Select a source to load the song.";

    [ObservableProperty]
    private string _previewTitleText = "No song loaded";

    [ObservableProperty]
    private string _previewArtistText = "-";

    [ObservableProperty]
    private string _previewMapText = "Map: -";

    [ObservableProperty]
    private string _previewFormatText = "Format: -";

    [ObservableProperty]
    private string _previewStatusText = "Waiting";

    [ObservableProperty]
    private string _toolTitleText = "Tool";

    [ObservableProperty]
    private string _toolDescriptionText = string.Empty;

    [ObservableProperty]
    private bool _isLogDrawerVisible;

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
            GuiPage.Target => "Select Target",
            GuiPage.Output => "Choose Output",
            GuiPage.Tool => "Tool",
            _ => "Select Song"
        };

        PrimaryActionText = value switch
        {
            GuiPage.Output => "Convert",
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

    partial void OnInputPathChanged(string value)
    {
        _ = UpdateInputPathAsync(resetSongSelection: true);
    }

    partial void OnDownloadAssetsWhenLoadingChanged(bool value)
    {
        if (!string.IsNullOrWhiteSpace(InputPath))
            _ = LoadPreviewAsync(resetSongSelection: false);
    }

    partial void OnSelectedPlatformChanged(PlatformItemViewModel? value)
    {
        LoadTargetsForPlatform(value);
    }

    partial void OnSelectedTargetChanged(TargetItemViewModel? value)
    {
        _ = UpdateSelectedTargetAsync(value);
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
    private void ShowOutputPage() => ShowPage(GuiPage.Output);

    [RelayCommand]
    private void Back()
    {
        ShowPage(CurrentPage switch
        {
            GuiPage.Target => GuiPage.Source,
            GuiPage.Output => GuiPage.Target,
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
                ShowPage(GuiPage.Target);
                break;
            case GuiPage.Target:
                ShowPage(GuiPage.Output);
                break;
            case GuiPage.Output:
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
        RebuildPrompts(ToolPrompts, selection.Tool.Prompts);
        ShowPage(GuiPage.Tool);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CancelAndDispose(ref _previewLoadCts);
        CancelAndDispose(ref _formatDetectionCts);
        CoverPreviewImage = null;
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

        SelectedPlatform = Platforms.FirstOrDefault();
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

        SelectedTarget = Targets.FirstOrDefault();
    }

    private async Task UpdateSelectedTargetAsync(TargetItemViewModel? target)
    {
        TargetSupportText = target is null ? string.Empty : GetSupportText(target.Target);
        RebuildPrompts(TargetPrompts, target?.Target.ExportPrompts ?? []);
        await RefreshCurrentPreviewAssetAsync();
    }

    private void RebuildPrompts(ObservableCollection<PromptInputViewModel> target, IReadOnlyList<ConversionPrompt> prompts)
    {
        target.Clear();
        foreach (ConversionPrompt prompt in prompts)
        {
            if (prompt.Id.Equals(ConversionPromptIds.OutputPath, StringComparison.OrdinalIgnoreCase))
                continue;

            target.Add(PromptInputViewModel.Create(prompt, _dialogs));
        }
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
        CancellationTokenSource cts = new();
        _previewLoadCts = cts;
        CancellationToken cancellationToken = cts.Token;

        if (resetSongSelection)
            ClearSongChoices();

        SetBusy(true, "Loading song...");

        try
        {
            PreviewImport preview = await ImportPreviewPackageAsync(inputPath, cancellationToken);
            if (_previewLoadCts != cts)
                return;

            _previewImportResult = preview.ImportResult;
            _previewLoadedFormatName = preview.FormatName;

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
            SongPreviewResult previewResult = await Task.Run(async () => await previewProvider.LoadPreviewAsync(new SongPreviewRequest(
                inputPath,
                _previewTempRoot,
                selectedSong,
                downloadOnlineAssets), cancellationToken), cancellationToken);

            JdiImportResult previewImportResult = new(
                previewResult.Package,
                previewResult.FormatName,
                previewResult.MaterializedRoot,
                previewResult.MaterializedRootIsTemporary);

            if (downloadOnlineAssets && previewImportResult.MaterializedRoot is not null)
                await Task.Run(async () => await new OnlineAssetDownloader(_logger).DownloadAssetsAsync(previewImportResult.MaterializedRoot, previewImportResult.Package), cancellationToken);

            return new PreviewImport(previewImportResult, previewResult.FormatName);
        }

        IJdiFormat sourceFormat = ResolveSourceFormat(inputPath);
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(sourceFormat.DisplayName);

        string previewInputPath = inputPath;
        string intermediatePath = Path.Combine(_previewTempRoot, "jdi");
        bool copyJdiForAssetDownloads = sourceFormat.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(inputPath)
            && downloadOnlineAssets;

        if (copyJdiForAssetDownloads)
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
        if (downloadOnlineAssets && importResult.MaterializedRoot is not null)
            await Task.Run(async () => await new OnlineAssetDownloader(_logger).DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package), cancellationToken);

        return new PreviewImport(importResult, sourceFormat.DisplayName);
    }

    private async Task RefreshCurrentPreviewAssetAsync()
    {
        if (_previewImportResult?.MaterializedRoot is null)
            return;

        try
        {
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
        PreviewData preview = await BuildPreviewDataAsync(importResult, target, cancellationToken);

        CoverPreviewImage = preview.Bitmap;
        CoverPreviewWidth = preview.Width;
        CoverPreviewHeight = preview.Height;
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

    private async Task<PreviewData> BuildPreviewDataAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition? target,
        CancellationToken cancellationToken)
    {
        if (importResult.MaterializedRoot is null)
            throw new InvalidOperationException("The import did not produce a materialized JDI package.");

        bool useWideCover = target is not null && UsesWideCover(target);

        IntermediateImageService imageService = new(
            importResult.MaterializedRoot,
            importResult.Package,
            new SystemFileSystem(),
            null,
            _logger);

        using Image<Bgra32> image = useWideCover
            ? await imageService.GetCoverAsync(640, 360, cancellationToken)
            : await imageService.GetSquareCoverAsync(512, 512, cancellationToken);

        Bitmap bitmap = await ToBitmapAsync(image, cancellationToken);
        return new PreviewData(bitmap, importResult.Package, useWideCover ? 640 : 512, useWideCover ? 360 : 512);
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

        PromptAnswerSet answers = BuildConversionAnswers();
        IConversionInteraction interaction = new StaticConversionInteraction(answers);
        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileNameWithoutExtension(inputPath) ?? "Export");

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(inputPath, intermediatePath, songName, Interaction: interaction));
        ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(new ConversionRequestContext(inputPath, outputPath, songName, target, answers, interaction));

        JdiImportResult importResult = await Task.Run(async () => await sourceFormat.ImportAsync(importRequest, cancellationToken), cancellationToken);
        try
        {
            if (downloadOnlineAssets && importResult.MaterializedRoot is not null)
                await Task.Run(async () => await new OnlineAssetDownloader(_logger).DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package), cancellationToken);

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
        CoverPreviewWidth = 512;
        CoverPreviewHeight = 512;
        PreviewPlaceholderText = message;
        IsPreviewPlaceholderVisible = true;
        PreviewTitleText = "No song loaded";
        PreviewArtistText = "-";
        PreviewMapText = "Map: -";
        PreviewFormatText = "Format: -";
        PreviewStatusText = "Waiting";
        _previewImportResult = null;
        _previewLoadedFormatName = null;
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

    private static bool UsesWideCover(ConversionTargetDefinition target) =>
        target.FormatName.Equals("JDNext PC", StringComparison.OrdinalIgnoreCase) ||
        target.FormatName.Equals("Unity", StringComparison.OrdinalIgnoreCase);

    private static string GetSupportText(ConversionTargetDefinition target) => target.SupportStatus switch
    {
        ConversionSupportStatus.Experimental => "Experimental target. Import/export may need extra verification.",
        ConversionSupportStatus.KnownPartial => "Known partial support. Basic conversion works, but platform-native dumps are still limited.",
        _ => string.Empty
    };

    private static async Task<Bitmap> ToBitmapAsync(Image<Bgra32> image, CancellationToken cancellationToken)
    {
        await using MemoryStream stream = new();
        await image.SaveAsync(stream, new PngEncoder(), cancellationToken);
        byte[] bytes = stream.ToArray();
        return new Bitmap(new MemoryStream(bytes));
    }

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

    private sealed record PreviewImport(JdiImportResult ImportResult, string FormatName);

    private sealed record PreviewData(Bitmap Bitmap, IntermediateSongPackage Package, int Width, int Height);
}
