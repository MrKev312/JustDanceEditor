using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;
using System.Windows.Input;

namespace JustDanceEditor.GUI.ViewModels;

public sealed partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = [];

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public bool IsEnabled { get; init; } = true;
}