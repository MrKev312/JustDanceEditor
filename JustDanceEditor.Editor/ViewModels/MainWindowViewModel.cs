using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Docking;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;

using KevInc.Avalonia;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly JustDanceDockFactory _factory;
    private readonly DockLayoutStorageService _layoutStorage;
    private readonly ITimelineContextService _timelineContext;
    private readonly List<IRelayCommand> _dynamicCommands = [];

    [ObservableProperty]
    public partial IRootDock? Layout { get; set; }

    public ObservableCollection<MenuItemViewModel> ViewMenu { get; } = [];

    public MainWindowViewModel(ITimelineContextService timelineContext)
    {
        _timelineContext = timelineContext ?? throw new ArgumentNullException(nameof(timelineContext));
        _timelineContext.PropertyChanged += TimelineContext_PropertyChanged;

        _layoutStorage = new DockLayoutStorageService();
        _factory = new JustDanceDockFactory(this);

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
            catch
            {
                _layoutStorage.SetLastLayoutName(null);
            }
        }

        try
        {
            SavedDockLayout? defaultLayout = _layoutStorage.TryLoadDefaultLayout();
            if (defaultLayout != null)
                return _factory.CreateLayout(defaultLayout);
        }
        catch
        {
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
        ViewMenu.Add(fileMenu);
    }

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

                if (Activator.CreateInstance(toolType) is IRunCommand rc)
                    return rc.CanRun(_timelineContext);
            }
            catch
            {
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
        if (Activator.CreateInstance(toolType) is not Tool tool)
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

        if (Activator.CreateInstance(type) is IRunCommand cmd)
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
        Window? topLevel = GetMainWindow();
        if (topLevel == null)
            return;

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
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
            catch
            {
            }
        }
    }

    public async Task OpenPackage(IntermediateSongPackage package, string rootPath)
    {
        IPlaybackService playback = new PlaybackService();
        App app = Avalonia.Application.Current as App ?? throw new InvalidOperationException("Application is not initialized.");
        TimelineSettingsService settings = (app.Services ?? throw new InvalidOperationException("Application services have not been initialized."))
            .GetRequiredService<TimelineSettingsService>();

        TimelineEditorViewModel editorVm = new(package, rootPath, playback, settings);
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

        string? name = await ShowTextPromptAsync("Save Layout", "Name", "");
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_layoutStorage.Exists(name))
        {
            bool overwrite = await ShowConfirmationAsync("Save Layout", $"Replace layout '{name}'?");
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
        string? newName = await ShowTextPromptAsync("Rename Layout", "Name", oldName);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return;

        if (_layoutStorage.Exists(newName))
        {
            bool overwrite = await ShowConfirmationAsync("Rename Layout", $"Replace layout '{newName}'?");
            if (!overwrite)
                return;
        }

        _layoutStorage.Rename(oldName, newName);
        BuildMenus();
    }

    private async Task DeleteLayoutAsync(string name)
    {
        bool delete = await ShowConfirmationAsync("Delete Layout", $"Delete layout '{name}'?");
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
        string baseId = new(title.Where(char.IsLetterOrDigit).ToArray());
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

    private static async Task<string?> ShowTextPromptAsync(string title, string label, string initialValue)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        PlatformTheme.ApplyFloatingWindowChrome(dialog);

        TextBox textBox = new()
        {
            Text = initialValue,
            MinWidth = 260
        };

        string? result = null;
        Button okButton = new() { Content = "OK", IsDefault = true, MinWidth = 80 };
        Button cancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        okButton.Click += (_, _) =>
        {
            result = textBox.Text?.Trim();
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = CreateDialogSurface(dialog, 12,
            new TextBlock { Text = label },
            textBox,
            new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Children = { okButton, cancelButton }
            });

        Window? owner = GetMainWindow();
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 380,
            Height = 145,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        PlatformTheme.ApplyFloatingWindowChrome(dialog);

        bool result = false;
        Button yesButton = new() { Content = "Yes", IsDefault = true, MinWidth = 80 };
        Button noButton = new() { Content = "No", IsCancel = true, MinWidth = 80 };
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        noButton.Click += (_, _) => dialog.Close();

        dialog.Content = CreateDialogSurface(dialog, 16,
            new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Children = { yesButton, noButton }
            });

        Window? owner = GetMainWindow();
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return result;
    }

    private static Border CreateDialogSurface(Control owner, double spacing, params Control[] children)
    {
        StackPanel panel = new()
        {
            Spacing = spacing
        };

        foreach (Control child in children)
            panel.Children.Add(child);

        return new Border
        {
            Background = FindBrush(owner, "JdeWindowBackgroundBrush", "#D820242A"),
            BorderBrush = FindBrush(owner, "JdeSurfaceBorderBrush", "#66FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = panel
        };
    }

    private static IBrush FindBrush(Control owner, string resourceKey, string fallbackColor)
    {
        if (owner.TryFindResource(resourceKey, null, out object? resource) && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallbackColor));
    }

    private static Window? GetMainWindow()
        => Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}

public class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = [];
    public System.Windows.Input.ICommand? Command { get; set; }
}
