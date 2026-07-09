using System;

namespace JustDanceEditor.Editor.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class InspectableAttribute(string displayName = "", string category = "General", bool isReadOnly = false) : Attribute
{
    public string DisplayName { get; set; } = displayName;
    public string Category { get; set; } = category;
    public bool IsReadOnly { get; set; } = isReadOnly;
}

public sealed class NumericInspectableAttribute(
    string displayName = "",
    string category = "General",
    double minimum = 0.0,
    double maximum = 1.0,
    double tickFrequency = 0.1,
    bool isReadOnly = false) : InspectableAttribute(displayName, category, isReadOnly)
{
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
    public double TickFrequency { get; } = tickFrequency;
}
