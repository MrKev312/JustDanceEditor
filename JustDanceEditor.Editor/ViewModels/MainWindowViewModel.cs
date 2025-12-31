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
    public IRootDock? Layout { get; set; }

    public ObservableCollection<MenuItemViewModel> ViewMenu { get; } = [];

    public MainWindowViewModel()
    {
        _timelineContext = ((App)Avalonia.Application.Current!).TimelineContext;

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
        fileMenu.Items.Add(new MenuItemViewModel
        {
            Header = "Open Map Folder...",
            Command = OpenMapCommand
        });
        ViewMenu.Add(fileMenu);
    }

    private void BuildDynamicMenu()
    {
        var toolTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Select(t => new { Type = t, Attr = t.GetCustomAttribute<ToolWindowAttribute>() })
            .Where(x => x.Attr != null)
            .OrderBy(x => x.Attr!.Title) // Sort by Title
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

        currentCollection.Add(new MenuItemViewModel
        {
            Header = title,
            Command = new RelayCommand(() => OpenTool(toolType, title))
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
                var editorVm = new TimelineEditorViewModel(package, path);

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