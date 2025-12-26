using System;

namespace JustDanceEditor.Editor.Attributes;

/// <param name="title">The name shown on the tab.</param>
/// <param name="category">The menu path, separated by slashes (e.g. "View/Windows").</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ToolWindowAttribute(string title, string category = "General") : Attribute
{
    public string Title { get; } = title;
    public string Category { get; } = category;
}