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

        LyricsCreationResult result = vm.Result ?? throw new InvalidOperationException("Expected a result after accepting the dialog.");
        Assert.Equal("Hello", result.Lyrics);
        Assert.Equal(48, result.Frames); // 2 beats * 24 fps
        Assert.True(result.IsEndOfLine);
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