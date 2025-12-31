using CommunityToolkit.Mvvm.ComponentModel;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Attributes;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("World", "Hello")]
public partial class WorldToolViewModel : Tool
{
    [ObservableProperty]
    public partial string Message { get; set; } = "Hello World from the Dock!";
}