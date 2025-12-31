using System;

namespace JustDanceEditor.Editor.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class InspectableAttribute(string displayName = "", string category = "General", bool isReadOnly = false) : Attribute
{
    public string DisplayName { get; set; } = displayName;
    public string Category { get; set; } = category;
    public bool IsReadOnly { get; set; } = isReadOnly;
}