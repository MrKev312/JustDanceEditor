using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Tests;

public class LyricsCreationViewModelTests
{
    [Fact]
    public void Accept_SetsResultCorrectly()
    {
        LyricsCreationViewModel vm = new()
        {
            Lyrics = "Hello",
            DurationBeats = 2.0M,
            IsEndOfLine = true
        };

        vm.Accept();

        Assert.NotNull(vm.Result);
        Assert.Equal("Hello", vm.Result!.Lyrics);
        Assert.Equal(48, vm.Result.Frames); // 2 beats * 24 fps
        Assert.True(vm.Result.IsEndOfLine);
    }

    [Fact]
    public void Cancel_SetsResultNull()
    {
        LyricsCreationViewModel vm = new()
        {
            Lyrics = "X"
        };
        vm.Accept();
        Assert.NotNull(vm.Result);
        vm.Cancel();
        Assert.Null(vm.Result);
    }
}