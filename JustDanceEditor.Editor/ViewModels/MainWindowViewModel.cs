using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Docking;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly JustDanceDockFactory _factory;
    private readonly DockLayoutStorageService _layoutStorage;
    private readonly ITimelineContextService _timelineContext;
    private readonly IDialogService _dialogService;
    private readonly IEditorPromptService _prompts;
    private readonly EditorSettingsService _editorSettings;
    private readonly TimelineSettingsService _timelineSettings;
    private readonly IWindowService _windows;
    private readonly IEditorObjectFactory _objects;
    private readonly IPlaybackServiceFactory _playbackFactory;
    private readonly ITimelinePictogramGenerator _pictogramGenerator;
    private readonly List<IRelayCommand> _dynamicCommands = [];
    private bool _disposed;

    [ObservableProperty]
    public partial IRootDock? Layout { get; set; }

    public ObservableCollection<MenuItemViewModel> ViewMenu { get; } = [];

    public MainWindowViewModel(
        ITimelineContextService timelineContext,
        IDialogService dialogService,
        IEditorPromptService prompts,
        EditorSettingsService editorSettings,
        TimelineSettingsService timelineSettings,
        IWindowService windows,
        IEditorObjectFactory objects,
        IPlaybackServiceFactory playbackFactory,
        ITimelinePictogramGenerator pictogramGenerator,
        DockLayoutStorageService layoutStorage)
    {
        _timelineContext = timelineContext ?? throw new ArgumentNullException(nameof(timelineContext));
        _dialogService = dialogService;
        _prompts = prompts;
        _editorSettings = editorSettings;
        _timelineSettings = timelineSettings;
        _windows = windows;
        _objects = objects;
        _playbackFactory = playbackFactory;
        _pictogramGenerator = pictogramGenerator;
        _timelineContext.PropertyChanged += TimelineContext_PropertyChanged;

        _layoutStorage = layoutStorage;
        _factory = new JustDanceDockFactory(this, objects, timelineContext);

        Layout = CreateStartupLayout();
        _factory.InitLayout(Layout);

        BuildMenus();
    }

    private IRootDock CreateStartupLayout()
    {
        string? lastLayoutName = _layoutStorage.GetLastLayoutName();
        if (!string.IsNullOrWhiteSpace(lastLayoutName))
        {
            try
            {
                return _factory.CreateLayout(_layoutStorage.Load(lastLayoutName));
            }
            catch (Exception ex)
            {
                EditorLog.Unexpected(ex, $"Load dock layout '{lastLayoutName}'");
                _layoutStorage.SetLastLayoutName(null);
            }
        }

        try
        {
            SavedDockLayout? defaultLayout = _layoutStorage.TryLoadDefaultLayout();
            if (defaultLayout != null)
                return _factory.CreateLayout(defaultLayout);
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Load default dock layout");
        }

        return _factory.CreateLayout();
    }

    private void BuildMenus()
    {
        ViewMenu.Clear();
        _dynamicCommands.Clear();

        CreateFileMenu();
        BuildDynamicMenu();
        BuildLayoutMenu();
    }

    private void CreateFileMenu()
    {
        MenuItemViewModel fileMenu = new() { Header = "File" };
        fileMenu.Items.Add(new MenuItemViewModel
        {
            Header = "Settings...",
            Command = new AsyncRelayCommand(OpenSettingsAsync)
        });
        ViewMenu.Add(fileMenu);
    }

    private async Task OpenSettingsAsync()
        => await _dialogService.ShowDialogAsync<bool>(new SettingsViewModel(_editorSettings));

    private void BuildDynamicMenu()
    {
        var toolTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Select(t => new { Type = t, Attr = t.GetCustomAttribute<RunCommandAttribute>() })
            .Where(x => x.Attr is not null)
            .Select(x => new { x.Type, Attr = x.Attr ?? throw new InvalidOperationException("RunCommandAttribute lookup unexpectedly returned null.") })
            .OrderByDescending(x => x.Attr.Priority)
            .ThenBy(x => x.Attr.Title)
            .ToList();

        foreach (var item in toolTypes)
            AddMenuPath(item.Attr.Category, item.Attr.Title, item.Type);
    }

    private void BuildLayoutMenu()
    {
        MenuItemViewModel layoutMenu = new() { Header = "Layout" };
        layoutMenu.Items.Add(new MenuItemViewModel
        {
            Header = "Save Layout...",
            Command = new AsyncRelayCommand(SaveLayoutAsync)
        });
        layoutMenu.Items.Add(new MenuItemViewModel
        {
            Header = "Reset to Default",
            Command = new RelayCommand(ResetLayout)
        });

        MenuItemViewModel savedLayoutsMenu = new() { Header = "Saved Layouts" };
        foreach (string layoutName in _layoutStorage.ListLayoutNames())
        {
            string capturedName = layoutName;
            MenuItemViewModel layoutItem = new() { Header = capturedName };
            layoutItem.Items.Add(new MenuItemViewModel
            {
                Header = "Load",
                Command = new RelayCommand(() => LoadLayout(capturedName))
            });
            layoutItem.Items.Add(new MenuItemViewModel
            {
                Header = "Rename...",
                Command = new AsyncRelayCommand(() => RenameLayoutAsync(capturedName))
            });
            layoutItem.Items.Add(new MenuItemViewModel
            {
                Header = "Delete",
                Command = new AsyncRelayCommand(() => DeleteLayoutAsync(capturedName))
            });
            savedLayoutsMenu.Items.Add(layoutItem);
        }

        layoutMenu.Items.Add(savedLayoutsMenu);
        ViewMenu.Add(layoutMenu);
    }

    private void AddMenuPath(string categoryPath, string title, Type toolType)
    {
        string[] parts = categoryPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        ObservableCollection<MenuItemViewModel> currentCollection = ViewMenu;

        foreach (string part in parts)
        {
            MenuItemViewModel? existing = currentCollection.FirstOrDefault(x => x.Header == part);
            if (existing == null)
            {
                existing = new MenuItemViewModel { Header = part };
                currentCollection.Add(existing);
            }

            currentCollection = existing.Items;
        }

        bool canExecute()
        {
            try
            {
                if (typeof(Tool).IsAssignableFrom(toolType))
                    return true;

                if (_objects.Create(toolType) is IRunCommand rc)
                    return rc.CanRun(_timelineContext);
            }
            catch (Exception ex)
            {
                EditorLog.Unexpected(ex, $"Evaluate command availability for {toolType.FullName}");
            }

            return false;
        }

        RelayCommand relay = new(() => ExecuteCommand(toolType, title), canExecute);
        _dynamicCommands.Add(relay);

        currentCollection.Add(new MenuItemViewModel
        {
            Header = title,
            Command = relay
        });
    }

    private void OpenTool(Type toolType, string title)
    {
        if (Layout == null)
            return;
        if (_objects.Create(toolType) is not Tool tool)
            return;

        tool.Id = CreateDockableId(title);
        tool.Title = title;

        if (_factory.FindMainDocumentDock(Layout) is IDock mainDock)
        {
            _factory.AddDockable(mainDock, tool);
            _factory.SetActiveDockable(tool);
            _factory.SetFocusedDockable(mainDock, tool);
        }
    }

    private void ExecuteCommand(Type type, string title)
    {
        if (Layout == null)
            return;

        if (typeof(Tool).IsAssignableFrom(type))
        {
            OpenTool(type, title);
            return;
        }

        if (_objects.Create(type) is IRunCommand cmd)
            cmd.Run(_timelineContext);
    }

    private void TimelineContext_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (IRelayCommand c in _dynamicCommands)
            c.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public async Task OpenMap()
    {
        IStorageProvider? storage = _windows.StorageProvider;
        if (storage == null)
            return;

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Map Folder",
            AllowMultiple = false
        });

        if (folders.Count == 1)
        {
            string path = folders[0].Path.LocalPath;
            try
            {
                IntermediateSongPackage package = IntermediatePackageSerializer.LoadFromFolder(path);
                await OpenPackage(package, path);
            }
            catch (Exception ex)
            {
                await _prompts.ShowErrorAsync(
                    "Error Opening Map",
                    $"Failed to open the selected map:\n{ex.Message}",
                    ex);
            }
        }
    }

    public async Task OpenPackage(IntermediateSongPackage package, string rootPath)
    {
        IPlaybackService playback = _playbackFactory.Create();
        TimelineEditorServices services = new(_timelineContext, _dialogService, _windows, _prompts);
        TimelineEditorViewModel editorVm = new(
            package,
            rootPath,
            playback,
            _timelineSettings,
            _pictogramGenerator,
            services);
        await editorVm.InitializeAsync();

        if (Layout != null && _factory.FindMainDocumentDock(Layout) is IDock mainDock)
        {
            _factory.AddDockable(mainDock, editorVm);
            _factory.SetActiveDockable(editorVm);
            _factory.SetFocusedDockable(mainDock, editorVm);

            _timelineContext.UpdateActiveTimeline(editorVm);
        }
    }

    private async Task SaveLayoutAsync()
    {
        if (Layout == null)
            return;

        string? name = await _prompts.PromptTextAsync("Save Layout", "Name");
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_layoutStorage.Exists(name))
        {
            bool overwrite = await _prompts.ConfirmAsync("Save Layout", $"Replace layout '{name}'?");
            if (!overwrite)
                return;
        }

        _layoutStorage.Save(_factory.CaptureLayout(name, Layout));
        _layoutStorage.SetLastLayoutName(name);
        BuildMenus();
    }

    private void LoadLayout(string name)
    {
        SavedDockLayout savedLayout = _layoutStorage.Load(name);
        ReplaceLayout(_factory.CreateLayout(savedLayout));
        _layoutStorage.SetLastLayoutName(name);
    }

    private async Task RenameLayoutAsync(string oldName)
    {
        string? newName = await _prompts.PromptTextAsync("Rename Layout", "Name", oldName);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return;

        if (_layoutStorage.Exists(newName))
        {
            bool overwrite = await _prompts.ConfirmAsync("Rename Layout", $"Replace layout '{newName}'?");
            if (!overwrite)
                return;
        }

        _layoutStorage.Rename(oldName, newName);
        BuildMenus();
    }

    private async Task DeleteLayoutAsync(string name)
    {
        bool delete = await _prompts.ConfirmAsync("Delete Layout", $"Delete layout '{name}'?");
        if (!delete)
            return;

        _layoutStorage.Delete(name);
        BuildMenus();
    }

    private void ResetLayout()
    {
        _layoutStorage.SetLastLayoutName(null);
        ReplaceLayout(CreateStartupLayout());
    }

    private void ReplaceLayout(IRootDock newLayout)
    {
        IRootDock? oldLayout = Layout;
        List<TimelineEditorViewModel> openTimelines = DetachTimelineDocuments(oldLayout);
        TimelineEditorViewModel? activeTimeline = _timelineContext.ActiveTimeline;

        _factory.InitLayout(newLayout);
        Layout = newLayout;

        TimelineEditorViewModel? restoredTimeline = null;
        if (openTimelines.Count > 0 && _factory.FindMainDocumentDock(newLayout) is IDock mainDock)
        {
            foreach (TimelineEditorViewModel timeline in openTimelines)
                _factory.AddDockable(mainDock, timeline);

            restoredTimeline = activeTimeline != null && openTimelines.Contains(activeTimeline)
                ? activeTimeline
                : openTimelines[^1];

            _factory.SetActiveDockable(restoredTimeline);
            _factory.SetFocusedDockable(mainDock, restoredTimeline);
        }

        _timelineContext.UpdateActiveTimeline(restoredTimeline);
        DisposeLayout(oldLayout);
    }

    private static List<TimelineEditorViewModel> DetachTimelineDocuments(IDockable? dockable)
    {
        List<TimelineEditorViewModel> timelines = [];
        DetachTimelineDocuments(dockable, timelines);
        return timelines;
    }

    private static void DetachTimelineDocuments(IDockable? dockable, List<TimelineEditorViewModel> timelines)
    {
        if (dockable == null)
            return;

        if (dockable is IRootDock root && root.Windows != null)
        {
            foreach (IDockWindow window in root.Windows)
                DetachTimelineDocuments(window.Layout, timelines);
        }

        if (dockable is not IDock dock || dock.VisibleDockables == null)
            return;

        foreach (IDockable child in dock.VisibleDockables.ToArray())
        {
            if (child is TimelineEditorViewModel timeline)
            {
                if (!timelines.Contains(timeline))
                    timelines.Add(timeline);
                dock.VisibleDockables.Remove(child);
                continue;
            }

            DetachTimelineDocuments(child, timelines);
        }

        if (dock.ActiveDockable is TimelineEditorViewModel)
            dock.ActiveDockable = dock.VisibleDockables.FirstOrDefault();
        if (dock.FocusedDockable is TimelineEditorViewModel)
            dock.FocusedDockable = dock.VisibleDockables.FirstOrDefault();
    }

    private static void DisposeLayout(IDockable? dockable)
    {
        if (dockable == null)
            return;

        if (dockable is IRootDock root && root.Windows != null)
        {
            foreach (IDockWindow window in root.Windows)
                DisposeLayout(window.Layout);
        }

        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (IDockable child in dock.VisibleDockables.ToArray())
                DisposeLayout(child);
        }

        if (dockable is IDisposable disposable)
            disposable.Dispose();
    }

    private string CreateDockableId(string title)
    {
        string baseId = new([.. title.Where(char.IsLetterOrDigit)]);
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "Tool";

        int index = 1;
        string candidate = baseId;
        while (Layout != null && _factory.FindDockable(Layout, d => d.Id == candidate) != null)
        {
            index++;
            candidate = $"{baseId}{index}";
        }

        return candidate;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timelineContext.PropertyChanged -= TimelineContext_PropertyChanged;
        DisposeLayout(Layout);
        Layout = null;
        GC.SuppressFinalize(this);
    }
}
