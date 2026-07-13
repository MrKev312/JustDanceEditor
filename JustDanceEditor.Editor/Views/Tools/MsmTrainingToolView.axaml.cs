using Avalonia.Controls;

using JustDanceEditor.Editor.ViewModels.Tools;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class MsmTrainingToolView : UserControl
{
    public MsmTrainingToolView()
    {
        InitializeComponent();
    }

    private async void OnTrainingCellClicked(object? sender, RecordingTrainingCellClickedEventArgs e)
    {
        if (DataContext is MsmTrainingToolViewModel viewModel)
            await viewModel.ToggleTrainingCellAsync(e.RowIndex, e.ColumnIndex);
    }
}
