using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Docking;

public class JustDanceDockFactory(MainWindowViewModel context) : Factory
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

    public override void SetActiveDockable(IDockable? dockable)
    {
        // If null, do not call base (base expects a non-null active dockable)
        if (dockable == null)
        {
            UpdateContext(null);
            return;
        }

        base.SetActiveDockable(dockable);
        UpdateContext(dockable);
    }

    public override void SetFocusedDockable(IDock? dock, IDockable? dockable)
    {
        // Guard against null dock to avoid passing null to base implementation
        if (dock == null)
        {
            UpdateContext(dockable);
            return;
        }

        base.SetFocusedDockable(dock, dockable);

        UpdateContext(dockable);
    }

    private void UpdateContext(IDockable? dockable)
    {
        if (dockable is TimelineEditorViewModel timeline)
        {
            if (Avalonia.Application.Current is App app)
            {
                app.TimelineContext.UpdateActiveTimeline(timeline);
            }
        }
    }
}