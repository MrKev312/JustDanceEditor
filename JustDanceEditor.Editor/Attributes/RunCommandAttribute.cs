using System;

namespace JustDanceEditor.Editor.Attributes;

/// <summary>
/// Attribute used to expose a type as a menu item that either opens a tool window
/// or runs a command when invoked.
/// </summary>
/// <param name="title">The name shown on the menu.</param>
/// <param name="category">The menu path, separated by slashes (e.g. "View/Windows").</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class RunCommandAttribute(string title, string category = "General") : Attribute
{
    public string Title { get; } = title;
    public string Category { get; } = category;
}