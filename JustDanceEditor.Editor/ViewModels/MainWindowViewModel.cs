using CommunityToolkit.Mvvm.Input;

using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Docking;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFactory _factory;
    public IRootDock? Layout { get; set; }

    public ObservableCollection<MenuItemViewModel> ViewMenu { get; } = [];

    public MainWindowViewModel()
    {
        // 1. Initialize Dock Factory
        _factory = new JustDanceDockFactory(this);

        // 2. Create Layout
        Layout = _factory.CreateLayout();
        if (Layout is not null)
        {
            _factory.InitLayout(Layout);
        }

        // 3. Scan for Tools and Build Menu
        BuildDynamicMenu();
    }

    private void BuildDynamicMenu()
    {
        // Find all types in this assembly that have the [ToolWindow] attribute
        IEnumerable<Type> toolTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttribute<ToolWindowAttribute>() != null);

        foreach (Type? type in toolTypes)
        {
            ToolWindowAttribute? attr = type.GetCustomAttribute<ToolWindowAttribute>();
            if (attr == null)
                continue;

            // Add to menu hierarchy
            AddMenuPath(attr.Category, attr.Title, type);
        }
    }

    private void AddMenuPath(string categoryPath, string title, Type toolType)
    {
        string[] parts = categoryPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        ObservableCollection<MenuItemViewModel> currentCollection = ViewMenu;

        // Traverse/Create categories
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

        // Add the actual item
        MenuItemViewModel toolItem = new()
        {
            Header = title,
            Command = new RelayCommand(() => OpenTool(toolType, title))
        };
        currentCollection.Add(toolItem);
    }

    private void OpenTool(Type toolType, string title)
    {
        if (Layout == null)
            return;

        if (Activator.CreateInstance(toolType) is not Tool tool)
            return;

        tool.Id = title.Replace(" ", "");
        tool.Title = title;

        // We look for our "MainDocumentDock" we defined in the Factory.
        if (_factory?.FindDockable(Layout, (d) => d.Id == "MainDocumentDock") is IDock mainDock)
        {
            _factory?.AddDockable(mainDock, tool);
            _factory?.SetActiveDockable(tool);
            _factory?.SetFocusedDockable(mainDock, tool);
        }
    }
}

// Simple helper for binding the menu
public class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = [];
    public System.Windows.Input.ICommand? Command { get; set; }
}