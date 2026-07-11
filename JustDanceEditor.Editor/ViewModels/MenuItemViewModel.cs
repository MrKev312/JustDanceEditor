using System.Collections.ObjectModel;
using System.Windows.Input;

namespace JustDanceEditor.Editor.ViewModels;

public sealed class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = [];
    public ICommand? Command { get; set; }
}
