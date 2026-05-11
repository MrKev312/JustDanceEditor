using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;

namespace JustDanceEditor.GUI.ViewModels;

public sealed partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _header = string.Empty;

    public ObservableCollection<MenuItemViewModel> Items { get; } = [];

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public bool IsEnabled { get; init; } = true;
}
