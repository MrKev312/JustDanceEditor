using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using DockWindow = Dock.Model.Mvvm.Core.DockWindow;

namespace JustDanceEditor.Editor.Docking;

public class JustDanceDockFactory(
    MainWindowViewModel context,
    IEditorObjectFactory objects,
    ITimelineContextService timelineContext) : Factory
{
    public const string MainDocumentDockId = "MainDocumentDock";

    public override IRootDock CreateLayout()
    {
        DocumentDock documentDock = CreateMainDocumentDock();

        ProportionalDock mainLayout = new()
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(documentDock)
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

    public IRootDock CreateLayout(SavedDockLayout savedLayout)
    {
        List<IDockable> children = [];
        if (savedLayout.Root != null)
        {
            foreach (SavedDockNode child in savedLayout.Root.Children)
            {
                IDockable? dockable = CreateDockable(child);
                if (dockable != null)
                    children.Add(dockable);
            }
        }

        if (!children.Any(ContainsMainDocumentDock))
            children.Insert(0, CreateMainDocumentDock());

        IDockable mainLayout = children.Count == 1
            ? children[0]
            : new ProportionalDock
            {
                Id = "MainLayout",
                Title = "Main Layout",
                Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList(children.ToArray())
            };

        List<IDockWindow> windows = [];
        foreach (SavedDockWindow savedWindow in savedLayout.Windows)
        {
            IRootDock? windowLayout = CreateWindowRoot(savedWindow.Layout);
            if (windowLayout == null)
                continue;

            windows.Add(new DockWindow
            {
                Id = savedWindow.Id ?? string.Empty,
                Title = savedWindow.Title ?? string.Empty,
                X = savedWindow.X,
                Y = savedWindow.Y,
                Width = savedWindow.Width,
                Height = savedWindow.Height,
                Topmost = savedWindow.Topmost,
                Layout = windowLayout
            });
        }

        RootDock root = new()
        {
            Id = "Root",
            Title = "Root",
            ActiveDockable = mainLayout,
            DefaultDockable = mainLayout,
            VisibleDockables = CreateList<IDockable>(mainLayout),
            Windows = CreateList(windows.ToArray())
        };

        return root;
    }

    public SavedDockLayout CaptureLayout(string name, IRootDock root)
    {
        SavedDockNode rootNode = new()
        {
            Kind = SavedDockNodeKind.Root,
            Id = root.Id,
            Title = root.Title
        };

        if (root.VisibleDockables != null)
        {
            foreach (IDockable dockable in root.VisibleDockables)
            {
                SavedDockNode? child = CaptureDockable(dockable);
                if (child != null)
                    rootNode.Children.Add(child);
            }
        }

        SavedDockLayout layout = new()
        {
            Name = name.Trim(),
            Root = rootNode
        };

        if (root.Windows != null)
        {
            foreach (IDockWindow window in root.Windows)
            {
                SavedDockNode? windowLayout = window.Layout == null ? null : CaptureDockable(window.Layout);
                if (windowLayout == null)
                    continue;

                layout.Windows.Add(new SavedDockWindow
                {
                    Id = window.Id,
                    Title = window.Title,
                    X = window.X,
                    Y = window.Y,
                    Width = window.Width,
                    Height = window.Height,
                    Topmost = window.Topmost,
                    Layout = windowLayout
                });
            }
        }

        return layout;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["JustDanceEditor.Editor.ViewModels.MainWindowViewModel"] = () => context
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new MainHostWindow()
        };

        base.InitLayout(layout);
    }

    public IDock? FindMainDocumentDock(IRootDock? layout)
        => layout == null ? null : FindDockable(layout, d => d.Id == MainDocumentDockId) as IDock;

    public override void SetActiveDockable(IDockable? dockable)
    {
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
        if (dock == null)
        {
            UpdateContext(dockable);
            return;
        }

        base.SetFocusedDockable(dock, dockable);

        UpdateContext(dockable);
    }

    private DocumentDock CreateMainDocumentDock()
        => new()
        {
            Id = MainDocumentDockId,
            Title = "Documents",
            Proportion = double.NaN,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>()
        };

    private RootDock? CreateWindowRoot(SavedDockNode? savedLayout)
    {
        if (savedLayout == null)
            return null;

        IDockable? layout = CreateDockable(savedLayout);
        if (layout == null)
            return null;

        return new RootDock
        {
            Id = savedLayout.Id ?? string.Empty,
            Title = savedLayout.Title ?? string.Empty,
            ActiveDockable = layout,
            DefaultDockable = layout,
            VisibleDockables = CreateList<IDockable>(layout)
        };
    }

    private IDockable? CreateDockable(SavedDockNode node)
    {
        return node.Kind switch
        {
            SavedDockNodeKind.Root => CreateRootChild(node),
            SavedDockNodeKind.ProportionalDock => ApplyCommon(node, new ProportionalDock
            {
                Orientation = ParseEnum(node.Orientation, Orientation.Horizontal),
                VisibleDockables = CreateList(CreateChildren(node).ToArray())
            }),
            SavedDockNodeKind.ToolDock => ApplyCommon(node, new ToolDock
            {
                Alignment = ParseEnum(node.Alignment, Alignment.Unset),
                GripMode = ParseEnum(node.GripMode, GripMode.Visible),
                IsExpanded = node.IsExpanded ?? true,
                AutoHide = node.AutoHide ?? false,
                VisibleDockables = CreateList(CreateChildren(node).ToArray())
            }),
            SavedDockNodeKind.DocumentDock => ApplyCommon(node, new DocumentDock
            {
                VisibleDockables = CreateList<IDockable>()
            }),
            SavedDockNodeKind.Splitter => ApplyCommon(node, new ProportionalDockSplitter
            {
                CanResize = node.CanResize ?? true,
                ResizePreview = node.ResizePreview ?? false
            }),
            SavedDockNodeKind.Tool => CreateTool(node),
            _ => null
        };
    }

    private IDockable? CreateRootChild(SavedDockNode node)
    {
        List<IDockable> children = CreateChildren(node);
        if (children.Count == 0)
            return null;

        return children.Count == 1 ? children[0] : ApplyCommon(node, new ProportionalDock
        {
            Orientation = ParseEnum(node.Orientation, Orientation.Horizontal),
            VisibleDockables = CreateList(children.ToArray())
        });
    }

    private List<IDockable> CreateChildren(SavedDockNode node)
    {
        List<IDockable> children = [];
        foreach (SavedDockNode child in node.Children)
        {
            IDockable? dockable = CreateDockable(child);
            if (dockable != null)
                children.Add(dockable);
        }

        return children;
    }

    private Tool? CreateTool(SavedDockNode node)
    {
        Type? toolType = ResolveToolType(node.ToolType);
        if (toolType == null || objects.Create(toolType) is not Tool tool)
            return null;

        return ApplyCommon(node, tool);
    }

    private SavedDockNode? CaptureDockable(IDockable dockable)
    {
        if (dockable is TimelineEditorViewModel)
            return null;

        SavedDockNode node;
        switch (dockable)
        {
            case Tool tool:
                node = CaptureCommon(dockable, SavedDockNodeKind.Tool);
                node.ToolType = tool.GetType().FullName;
                return node;

            case IDocumentDock:
                node = CaptureCommon(dockable, SavedDockNodeKind.DocumentDock);
                node.Id ??= MainDocumentDockId;
                return node;

            case IProportionalDock proportionalDock:
                node = CaptureDock(dockable, SavedDockNodeKind.ProportionalDock);
                node.Orientation = proportionalDock.Orientation.ToString();
                return node;

            case IToolDock toolDock:
                node = CaptureDock(dockable, SavedDockNodeKind.ToolDock);
                node.Alignment = toolDock.Alignment.ToString();
                node.GripMode = toolDock.GripMode.ToString();
                node.IsExpanded = toolDock.IsExpanded;
                node.AutoHide = toolDock.AutoHide;
                return node;

            case IProportionalDockSplitter splitter:
                node = CaptureCommon(dockable, SavedDockNodeKind.Splitter);
                node.CanResize = splitter.CanResize;
                node.ResizePreview = splitter.ResizePreview;
                return node;

            case IDock:
                node = CaptureDock(dockable, SavedDockNodeKind.ProportionalDock);
                node.Orientation = Orientation.Horizontal.ToString();
                return node.Children.Count == 0 ? null : node;

            default:
                return null;
        }
    }

    private SavedDockNode CaptureDock(IDockable dockable, SavedDockNodeKind kind)
    {
        SavedDockNode node = CaptureCommon(dockable, kind);
        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (IDockable childDockable in dock.VisibleDockables)
            {
                SavedDockNode? child = CaptureDockable(childDockable);
                if (child != null)
                    node.Children.Add(child);
            }
        }

        return node;
    }

    private static SavedDockNode CaptureCommon(IDockable dockable, SavedDockNodeKind kind)
        => new()
        {
            Kind = kind,
            Id = dockable.Id,
            Title = dockable.Title,
            Proportion = double.IsNaN(dockable.Proportion) ? null : dockable.Proportion,
            IsCollapsable = dockable.IsCollapsable,
            CanClose = dockable.CanClose,
            CanFloat = dockable.CanFloat,
            CanDrag = dockable.CanDrag,
            CanDrop = dockable.CanDrop,
            CanPin = dockable.CanPin
        };

    private static T ApplyCommon<T>(SavedDockNode node, T dockable)
        where T : IDockable
    {
        dockable.Id = node.Id ?? string.Empty;
        dockable.Title = node.Title ?? string.Empty;
        if (node.Proportion.HasValue)
            dockable.Proportion = node.Proportion.Value;
        if (node.IsCollapsable.HasValue)
            dockable.IsCollapsable = node.IsCollapsable.Value;
        if (node.CanClose.HasValue)
            dockable.CanClose = node.CanClose.Value;
        if (node.CanFloat.HasValue)
            dockable.CanFloat = node.CanFloat.Value;
        if (node.CanDrag.HasValue)
            dockable.CanDrag = node.CanDrag.Value;
        if (node.CanDrop.HasValue)
            dockable.CanDrop = node.CanDrop.Value;
        if (node.CanPin.HasValue)
            dockable.CanPin = node.CanPin.Value;

        return dockable;
    }

    private static bool ContainsMainDocumentDock(IDockable dockable)
    {
        if (dockable.Id == MainDocumentDockId)
            return true;

        return dockable is IDock dock
            && dock.VisibleDockables != null
            && dock.VisibleDockables.Any(ContainsMainDocumentDock);
    }

    private static Type? ResolveToolType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        return Assembly.GetExecutingAssembly().GetType(typeName)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(type => type != null);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
        => Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;

    private void UpdateContext(IDockable? dockable)
    {
        if (dockable is TimelineEditorViewModel timeline)
            timelineContext.UpdateActiveTimeline(timeline);
    }
}
