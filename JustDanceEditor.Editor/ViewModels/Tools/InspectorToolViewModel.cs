using CommunityToolkit.Mvvm.ComponentModel;

using Dock.Model.Mvvm.Controls;

using JustDanceEditor.Editor.Attributes;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Properties", "Other/Debugging")]
public partial class InspectorToolViewModel : Tool
{
    [ObservableProperty]
    private string _targetName = "None selected";
}