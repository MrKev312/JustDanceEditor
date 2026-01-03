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

        Assert.NotNull(vm.Result);
        Assert.Equal("m2", vm.Result!.MoveId);
        Assert.Equal(48, vm.Result.Frames);
        Assert.True(vm.Result.IsGold);
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