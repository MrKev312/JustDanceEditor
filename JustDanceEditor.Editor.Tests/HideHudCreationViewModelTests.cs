using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Tests;

public class HideHudCreationViewModelTests
{
    [Fact]
    public void Accept_SetsResultCorrectly()
    {
        HideHudCreationViewModel vm = new()
        {
            DurationBeats = 2.0M
        };

        vm.Accept();

        Assert.NotNull(vm.Result);
        Assert.Equal(48, vm.Result!.Frames); // 2 beats * 24 fps
    }

    [Fact]
    public void Cancel_SetsResultNull()
    {
        HideHudCreationViewModel vm = new();
        vm.Accept();
        Assert.NotNull(vm.Result);
        vm.Cancel();
        Assert.Null(vm.Result);
    }
}
