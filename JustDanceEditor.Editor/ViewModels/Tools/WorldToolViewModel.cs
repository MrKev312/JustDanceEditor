using CommunityToolkit.Mvvm.ComponentModel;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Attributes;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("World", "Hello")]
public partial class WorldToolViewModel : Tool
{
    [ObservableProperty]
    private string _message = "Hello World from the Dock!";
}