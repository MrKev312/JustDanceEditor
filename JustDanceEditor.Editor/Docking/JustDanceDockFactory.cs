using Avalonia.Controls;

using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Views;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Docking;

public class JustDanceDockFactory(object context) : Factory
{
    public override IRootDock CreateLayout()
    {
        DocumentDock documentDock = new()
        {
            Id = "MainDocumentDock",
            Title = "Documents",
            Proportion = double.NaN,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>()
        };

        ProportionalDock mainLayout = new()
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                documentDock
            )
        };

        RootDock root = new()
        {
            Id = "Root",
            Title = "Root",
            ActiveDockable = mainLayout,
            DefaultDockable = mainLayout,
            VisibleDockables = CreateList<IDockable>(mainLayout)
        };

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["JustDanceEditor.Editor.ViewModels.MainWindowViewModel"] = () => context
        };

        // Use the custom MainHostWindow which has the properties we want
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new MainHostWindow()
        };

        base.InitLayout(layout);
    }
}