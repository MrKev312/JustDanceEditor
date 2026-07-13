using Avalonia.Controls;
using Avalonia.Input;

using JustDanceEditor.Editor.ViewModels.Tools;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class RecordingsToolView : UserControl
{
    public RecordingsToolView()
    {
        InitializeComponent();
    }

    private void OnMoveGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: RecordingMoveScoreViewModel move }
            && DataContext is RecordingsToolViewModel viewModel)
        {
            viewModel.FocusMove(move.MoveIndex);
            e.Handled = true;
        }
    }

    private void OnAggregateGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: RecordingMoveAggregateViewModel aggregate }
            && DataContext is RecordingsToolViewModel viewModel)
        {
            viewModel.FocusMove(aggregate.WorstMoveIndex);
            e.Handled = true;
        }
    }

    private void OnIssueGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: RecordingIssueViewModel issue }
            && issue.MoveIndex.HasValue
            && DataContext is RecordingsToolViewModel viewModel)
        {
            viewModel.FocusMove(issue.MoveIndex.Value);
            e.Handled = true;
        }
    }

}