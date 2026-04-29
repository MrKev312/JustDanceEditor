using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Tests;

public class PictogramScreenshotOptionsViewModelTests
{
    [Fact]
    public void Accept_DefaultsToNoneInsertion_WhenInsertionOptionsHidden()
    {
        PictogramScreenshotOptionsViewModel vm = new();
        vm.SelectedInsertionMode = PictogramInsertionMode.AllInstancesOfSelectedMove;

        vm.Accept();

        PictogramScreenshotOptionsResult result = vm.Result ?? throw new InvalidOperationException("Expected result");
        Assert.Equal(PictogramInsertionMode.None, result.InsertionMode);
        Assert.Null(result.ReferenceMoveId);
        Assert.Null(result.ReferenceMoveStartFrame);
    }

    [Fact]
    public void Accept_AllInstances_UsesSelectedReferenceMove()
    {
        PictogramScreenshotOptionsViewModel vm = new();
        vm.EnableInsertionOptions([
            new PictogramReferenceMoveOption { MoveId = "moveA", StartFrame = 96 },
            new PictogramReferenceMoveOption { MoveId = "moveB", StartFrame = 192 }
        ]);

        vm.SelectedInsertionMode = PictogramInsertionMode.AllInstancesOfSelectedMove;
        vm.SelectedReferenceMove = vm.ReferenceMoveOptions[1];

        vm.Accept();

        PictogramScreenshotOptionsResult result = vm.Result ?? throw new InvalidOperationException("Expected result");
        Assert.Equal(PictogramInsertionMode.AllInstancesOfSelectedMove, result.InsertionMode);
        Assert.Equal("moveB", result.ReferenceMoveId);
        Assert.Equal(192, result.ReferenceMoveStartFrame);
    }

    [Fact]
    public void EnableInsertionOptions_PreservesIncomingOrder()
    {
        PictogramScreenshotOptionsViewModel vm = new();

        vm.EnableInsertionOptions([
            new PictogramReferenceMoveOption { MoveId = "moveFar", StartFrame = 600 },
            new PictogramReferenceMoveOption { MoveId = "moveNear", StartFrame = 120 }
        ]);

        Assert.Equal("moveFar", vm.ReferenceMoveOptions[0].MoveId);
        Assert.Equal("moveNear", vm.ReferenceMoveOptions[1].MoveId);
    }
}
