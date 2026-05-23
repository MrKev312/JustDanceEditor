using System.Collections.Generic;

namespace JustDanceEditor.Editor.Docking;

public sealed class SavedDockLayout
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public SavedDockNode? Root { get; set; }
    public List<SavedDockWindow> Windows { get; set; } = [];
}

public sealed class SavedDockNode
{
    public SavedDockNodeKind Kind { get; set; }
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? ToolType { get; set; }
    public double? Proportion { get; set; }
    public bool? IsCollapsable { get; set; }
    public bool? CanClose { get; set; }
    public bool? CanFloat { get; set; }
    public bool? CanDrag { get; set; }
    public bool? CanDrop { get; set; }
    public bool? CanPin { get; set; }
    public string? Orientation { get; set; }
    public string? Alignment { get; set; }
    public string? GripMode { get; set; }
    public bool? IsExpanded { get; set; }
    public bool? AutoHide { get; set; }
    public bool? CanResize { get; set; }
    public bool? ResizePreview { get; set; }
    public List<SavedDockNode> Children { get; set; } = [];
}

public sealed class SavedDockWindow
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Topmost { get; set; }
    public SavedDockNode? Layout { get; set; }
}

public enum SavedDockNodeKind
{
    Root,
    ProportionalDock,
    ToolDock,
    DocumentDock,
    Splitter,
    Tool
}
