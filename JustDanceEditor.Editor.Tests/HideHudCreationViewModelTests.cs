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

        HideHudCreationResult result = vm.Result ?? throw new InvalidOperationException("Expected a result after accepting the dialog.");
        Assert.Equal(48, result.Frames); // 2 beats * 24 fps
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