using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Tests;

public class MoveCreationViewModelTests
{
    [Fact]
    public void Accept_SetsResultWithFramesAndGold()
    {
        MoveCreationViewModel vm = new(["m1", "m2"], isFullBody: false)
        {
            SelectedMove = "m2",
            IsGold = true
        };
        vm.SetDurationDisplay("2");

        vm.Accept(48);

        MoveCreationResult result = vm.Result ?? throw new InvalidOperationException("Expected a result after accepting the dialog.");
        Assert.Equal("m2", result.MoveId);
        Assert.Equal(48, result.Frames);
        Assert.True(result.IsGold);
    }

    [Fact]
    public void Cancel_SetsResultNull()
    {
        MoveCreationViewModel vm = new(["m1"], false)
        {
            SelectedMove = "m1"
        };
        vm.Accept(24);
        Assert.NotNull(vm.Result);
        vm.Cancel();
        Assert.Null(vm.Result);
    }
}