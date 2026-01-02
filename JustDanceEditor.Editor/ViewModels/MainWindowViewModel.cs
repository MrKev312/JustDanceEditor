using Avalonia.Controls;
using Avalonia.Platform.Storage;

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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFactory _factory;
    private readonly ITimelineContextService _timelineContext;
    private readonly List<IRelayCommand> _dynamicCommands = [];
    public IRootDock? Layout { get; set; }

    public ObservableCollection<MenuItemViewModel> ViewMenu { get; } = [];

    public MainWindowViewModel()
    {
        _timelineContext = ((App)Avalonia.Application.Current!).TimelineContext ?? throw new InvalidOperationException("TimelineContext must not be null");
        _timelineContext.PropertyChanged += TimelineContext_PropertyChanged;

        // 1. Initialize Dock Factory
        _factory = new JustDanceDockFactory(this);

        // 2. Create Layout
        Layout = _factory.CreateLayout();
        if (Layout is not null)
        {
            _factory.InitLayout(Layout);
        }

        // 3. Build Menus
        CreateFileMenu();
        BuildDynamicMenu();
    }

    private void CreateFileMenu()
    {
        MenuItemViewModel fileMenu = new() { Header = "File" };
        ViewMenu.Add(fileMenu);
    }

    private void BuildDynamicMenu()
    {
        _dynamicCommands.Clear();

        var toolTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Select(t => new { Type = t, Attr = t.GetCustomAttribute<RunCommandAttribute>() })
            .Where(x => x.Attr != null)
            // Sort first by priority (descending - higher priority first), then by Title
            .OrderByDescending(x => x.Attr!.Priority)
            .ThenBy(x => x.Attr!.Title)
            .ToList();

        foreach (var item in toolTypes)
        {
            AddMenuPath(item.Attr!.Category, item.Attr!.Title, item.Type);
        }
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

        // Create a command that runs or opens a tool. Provide a canExecute that checks IRunCommand.CanRun when appropriate.
        Func<bool> canExecute = () =>
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
                // Swallow; if instantiation fails, default to disabled
            }

            return false;
        };

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

        tool.Id = title.Replace(" ", "");
        tool.Title = title;

        if (_factory?.FindDockable(Layout, (d) => d.Id == "MainDocumentDock") is IDock mainDock)
        {
            _factory?.AddDockable(mainDock, tool);
            _factory?.SetActiveDockable(tool);
            _factory?.SetFocusedDockable(mainDock, tool);
        }
    }

    private void ExecuteCommand(Type type, string title)
    {
        if (Layout == null)
            return;

        // If it's a tool (UI), open it
        if (typeof(Tool).IsAssignableFrom(type))
        {
            OpenTool(type, title);
            return;
        }

        // Otherwise try to create and run IRunCommand
        if (Activator.CreateInstance(type) is IRunCommand cmd)
        {
            cmd.Run(_timelineContext);
        }
    }

    private void TimelineContext_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (IRelayCommand c in _dynamicCommands)
        {
            c.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public async Task OpenMap()
    {
        Window? topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

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
                TimelineEditorViewModel editorVm = new(package, path);

                if (_factory?.FindDockable(Layout!, (d) => d.Id == "MainDocumentDock") is IDock mainDock)
                {
                    _factory?.AddDockable(mainDock, editorVm);
                    _factory?.SetActiveDockable(editorVm);
                    _factory?.SetFocusedDockable(mainDock, editorVm);

                    _timelineContext.UpdateActiveTimeline(editorVm);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
    }
}

public class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = [];
    public System.Windows.Input.ICommand? Command { get; set; }
}