using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Editor.Views.Tools;

namespace JustDanceEditor.Editor.Tests;

public sealed class MsmTrainingToolViewModelTests
{
    [AvaloniaFact]
    public void ClearingCoachList_AllowsComboBoxToClearSelectionWithoutValidationError()
    {
        MsmTrainingToolViewModel viewModel = new();
        viewModel.CoachIds.Add(0);
        viewModel.SelectedCoachId = 0;

        MsmTrainingToolView view = new() { DataContext = viewModel };
        Window window = new() { Content = view };

        try
        {
            window.Show();
            ComboBox comboBox = view.GetLogicalDescendants().OfType<ComboBox>().Single();

            viewModel.CoachIds.Clear();

            Assert.Null(viewModel.SelectedCoachId);
            Assert.False(DataValidationErrors.GetHasErrors(comboBox));
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }
}