using JustDanceEditor.AppHost;
using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.GUI.ViewModels;

public sealed record PlatformItemViewModel(PlatformDescriptor Platform)
{
    public override string ToString() => Platform.DisplayName;
}

public sealed record TargetItemViewModel(ConversionTargetDefinition Target)
{
    public override string ToString() => ConversionTargetSelector.FormatTargetLabel(Target);
}

public sealed record SongItemViewModel(string Name)
{
    public override string ToString() => Name;
}

public sealed record PromptOptionItemViewModel(PromptOption Option)
{
    public override string ToString() => Option.Label;
}

public sealed record ToolMenuItemViewModel(IToolProvider Provider, ToolDefinition Tool)
{
    public string Title => Tool.DisplayName;
}
