using System.Collections.ObjectModel;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public sealed class PropertyCategoryViewModel(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<PropertyItemViewModel> Properties { get; } = [];
}
