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
using JustDanceEditor.GUI.ViewModels.Pages;

using KevInc.Avalonia.Logging;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;

namespace JustDanceEditor.GUI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IJdiFormat[] _formats;
    private readonly IFormatConversionStrategy[] _strategies;
    private readonly IToolProvider[] _toolProviders;
    private readonly ISongPreviewProvider[] _previewProviders;
    private readonly CoverPreviewBuilder _coverPreviewBuilder;
    private readonly UiLogBuffer _logBuffer;
    private readonly IApplicationDialogService _dialogs;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly List<MenuItemViewModel> _menuRoots = [];

    private CancellationTokenSource? _previewLoadCts;
    private CancellationTokenSource? _formatDetectionCts;
    private CancellationTokenSource? _outputDetectionCts;
    private CancellationTokenSource? _previewOnlineAssetCts;
    private JdiImportResult? _previewImportResult;
    private string? _previewLoadedFormatName;
    private Task? _previewAssetWarmupTask;
    private string? _previewTempRoot;
    private MenuItemViewModel? _activityLogMenuItem;
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
        SourcePage = new SourcePageViewModel(dialogs);
        TargetPage = new TargetPageViewModel(_strategies, dialogs);
        ToolPage = new ToolPageViewModel(dialogs);
        Preview = new PreviewPanelViewModel(_coverPreviewBuilder);

        SubscribePageEvents();
        BuildMenus();
        ClearPreview("Select a source to load the song.");
        ShowPage(GuiPage.Source);
        _ = UpdateDetectedFormatAsync();
    }

    public ObservableCollection<MenuItemViewModel> MainMenu { get; } = [];

    public SourcePageViewModel SourcePage { get; }

    public TargetPageViewModel TargetPage { get; }

    public ToolPageViewModel ToolPage { get; }

    public PreviewPanelViewModel Preview { get; }

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

    public bool CanUsePrimaryAction => CanPrimaryAction();

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
        NotifyPrimaryActionStateChanged();

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
        NotifyPrimaryActionStateChanged();
    }

    private void SubscribePageEvents()
    {
        SourcePage.InputPathUpdated += (_, _) =>
        {
            _ = UpdateInputPathAsync(resetSongSelection: true);
            NotifyPrimaryActionStateChanged();
        };

        SourcePage.OutputPathUpdated += (_, _) =>
        {
            _ = UpdateDetectedOutputAsync();
            NotifyPrimaryActionStateChanged();
        };

        SourcePage.DownloadAssetsWhenLoadingUpdated += (_, _) =>
        {
            if (SourcePage.DownloadAssetsWhenLoading)
                StartPreviewOnlineAssetRequest();
            else
                CancelAndDispose(ref _previewOnlineAssetCts);

            UpdateTargetPreviewContext();
            _ = RefreshCurrentPreviewAssetAsync();
        };

        SourcePage.SelectedSongUpdated += (_, _) => _ = LoadPreviewAsync(resetSongSelection: false);

        TargetPage.SelectedTargetUpdated += (_, _) =>
        {
            NotifyPrimaryActionStateChanged();
            _ = RefreshCurrentPreviewAssetAsync();
        };

        TargetPage.CoverOptionsUpdated += (_, _) => _ = RefreshCurrentPreviewAssetAsync();
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
    private void Back()
    {
        ShowPage(CurrentPage switch
        {
            GuiPage.Target => GuiPage.Source,
            GuiPage.Tool => GuiPage.Source,
            _ => GuiPage.Source
        });
    }

    [RelayCommand(CanExecute = nameof(CanPrimaryAction))]
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

        ToolPage.Load(selection);
        ShowPage(GuiPage.Tool);
        NotifyPrimaryActionStateChanged();
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
        Preview.Dispose();
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
        SourcePage.RequireInputPath();
        SourcePage.RequireOutputPath();
        await UpdateDetectedOutputAsync();
        ShowPage(GuiPage.Target);
    }

    private bool CanPrimaryAction()
    {
        if (IsBusy)
            return false;

        return CurrentPage switch
        {
            GuiPage.Source => SourcePage.CanContinue,
            GuiPage.Target => TargetPage.CanConvert,
            GuiPage.Tool => ToolPage.HasSelection,
            _ => false
        };
    }

    private void NotifyPrimaryActionStateChanged()
    {
        OnPropertyChanged(nameof(CanUsePrimaryAction));
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private void UpdateTargetPreviewContext()
    {
        TargetPage.UpdatePreviewContext(
            _previewLoadedFormatName ?? _previewImportResult?.SourceFormat,
            _previewImportResult?.MaterializedRoot,
            SourcePage.DownloadAssetsWhenLoading);
    }

    private async Task UpdateDetectedFormatAsync()
    {
        string path = SourcePage.NormalizedInputPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            SourcePage.DetectedFormatText = "No source selected.";
            return;
        }

        CancelAndDispose(ref _formatDetectionCts);
        CancellationTokenSource cts = new();
        _formatDetectionCts = cts;
        CancellationToken cancellationToken = cts.Token;

        SourcePage.DetectedFormatText = "Detecting source format...";

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

            SourcePage.DetectedFormatText = detected.Length switch
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
            SourcePage.DetectedFormatText = $"Source detection failed: {ex.Message}";
        }
    }

    private async Task UpdateDetectedOutputAsync()
    {
        string path = SourcePage.NormalizedOutputPath;
        CancelAndDispose(ref _outputDetectionCts);

        if (string.IsNullOrWhiteSpace(path))
        {
            SourcePage.DetectedOutputText = "No output selected.";
            return;
        }

        CancellationTokenSource cts = new();
        _outputDetectionCts = cts;
        CancellationToken cancellationToken = cts.Token;

        SourcePage.DetectedOutputText = "Detecting output target...";

        try
        {
            OutputTargetDetectionResult result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return OutputTargetDetector.Detect(path, _formats, TargetPage.AvailableTargets);
            }, cancellationToken);

            if (_outputDetectionCts != cts || cancellationToken.IsCancellationRequested)
                return;

            SourcePage.DetectedOutputText = result.Message;
            if (result.Target is not null)
                TargetPage.SelectTarget(result.Target);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SourcePage.DetectedOutputText = $"Output detection failed: {ex.Message}";
        }
    }

    private async Task LoadPreviewAsync(bool resetSongSelection)
    {
        string inputPath = SourcePage.NormalizedInputPath;
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
            SourcePage.ClearSongChoices();

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

            UpdateTargetPreviewContext();
            Task onlineAssetTask = DownloadOnlineAssetsForPreviewAsync(preview.ImportResult, TargetPage.CurrentTarget, cancellationToken);
            await WaitForPreviewAssetWarmupIfNeededAsync(preview.AssetWarmupTask, TargetPage.CurrentTarget, preview.FormatName, cancellationToken);
            await onlineAssetTask;
            UpdateTargetPreviewContext();
            await DisplayPreviewAsync(preview.ImportResult, TargetPage.CurrentTarget, preview.FormatName, cancellationToken);
            StatusText = "Song loaded.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (MultipleConversionItemsFoundException ex)
        {
            SourcePage.ShowSongChoices(ex.AvailableItems);
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

        string? selectedSong = SourcePage.GetSongName();
        bool downloadOnlineAssets = SourcePage.DownloadAssetsWhenLoading;
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
            await WaitForPreviewAssetWarmupIfNeededAsync(_previewAssetWarmupTask, TargetPage.CurrentTarget, _previewLoadedFormatName ?? _previewImportResult.SourceFormat, CancellationToken.None);
            if (rebuildGeneratorOptions)
                UpdateTargetPreviewContext();
            await DisplayPreviewAsync(_previewImportResult, TargetPage.CurrentTarget, _previewLoadedFormatName ?? "JDI", CancellationToken.None);
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
        await Preview.DisplayPreviewAsync(
            importResult,
            target,
            TargetPage.CreateCoverPreviewOptions(),
            sourceFormatName,
            cancellationToken);
    }

    private async Task ConvertAsync(CancellationToken cancellationToken)
    {
        string inputPath = SourcePage.RequireInputPath();
        string outputPath = SourcePage.RequireOutputPath();
        string? songName = SourcePage.GetSongName();
        ConversionTargetDefinition target = TargetPage.CurrentTarget ?? throw new InvalidOperationException("Select a target first.");
        IJdiFormat sourceFormat = ResolveSourceFormat(inputPath);
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(sourceFormat.DisplayName);
        IFormatConversionStrategy targetStrategy = ResolveStrategy(target.FormatName);
        IJdiFormat targetFormat = ResolveFormat(target.FormatName);
        bool downloadOnlineAssets = SourcePage.DownloadAssetsWhenLoading;

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
        ToolMenuItemViewModel selection = ToolPage.Selection ?? throw new InvalidOperationException("Select a tool from the Tools menu first.");
        PromptAnswerSet answers = ToolPage.BuildAnswers();
        IConversionInteraction interaction = new GuiConversionInteraction(_dialogs, answers);
        await Task.Run(async () => await selection.Provider.ExecuteAsync(new ToolExecutionContext(selection.Tool, answers, interaction), cancellationToken), cancellationToken);
    }

    private PromptAnswerSet BuildPreviewAnswers(string previewRoot)
    {
        PromptAnswerSet answers = new();
        answers.Set(ConversionPromptIds.OutputPath, previewRoot);

        string? songName = SourcePage.GetSongName();
        if (!string.IsNullOrWhiteSpace(songName))
            answers.Set("ubiart.songName", songName);

        return answers;
    }

    private void StartPreviewOnlineAssetRequest()
    {
        CancelAndDispose(ref _previewOnlineAssetCts);

        if (!SourcePage.DownloadAssetsWhenLoading || _previewImportResult?.MaterializedRoot is null)
            return;

        JdiImportResult importResult = _previewImportResult;
        CancellationTokenSource cts = new();
        _previewOnlineAssetCts = cts;
        CancellationToken cancellationToken = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await DownloadOnlineAssetsForPreviewAsync(importResult, TargetPage.CurrentTarget, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                Dispatcher.UIThread.Post(async () =>
                {
                    if (_isDisposed ||
                        !ReferenceEquals(_previewOnlineAssetCts, cts) ||
                        !ReferenceEquals(_previewImportResult, importResult))
                    {
                        return;
                    }

                    UpdateTargetPreviewContext();
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
        if (!SourcePage.DownloadAssetsWhenLoading || importResult.MaterializedRoot is null)
            return;

        OnlineAssetDefinition[] requestedAssets =
        [
            .. CoverRules.GetRequiredOnlineAssetsForSelection(
                target,
                TargetPage.ResolveSquareCoverGeneratorKind(),
                TargetPage.ResolveWideCoverGeneratorKind())
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
        return TargetPage.BuildConversionAnswers(SourcePage.RequireOutputPath(), SourcePage.GetSongName());
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
            SourcePage.ShowSongChoices(ex.AvailableItems);
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

    private void ClearPreview(string message)
    {
        Preview.Clear(message);
        _previewImportResult = null;
        _previewLoadedFormatName = null;
        _previewAssetWarmupTask = null;
        TargetPage.ResetCoverSourceSelections();
        UpdateTargetPreviewContext();
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
                TargetPage.ResolveSquareCoverGeneratorKind(),
                TargetPage.ResolveWideCoverGeneratorKind()))
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
            TargetPage.CreateCoverPreviewOptions(),
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

    private sealed record PreviewImport(JdiImportResult ImportResult, string FormatName, Task? AssetWarmupTask);
}