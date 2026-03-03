using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI.Timelines;

using Moq;

namespace JustDanceEditor.Editor.Tests;

public class PropertyItemViewModelTests
{
    [Fact]
    public void SettingSameValue_DoesNotRecordUndo()
    {
        Mock<IUndoService> undoMock = new();
        KaraokeClip clipObj = new() { Lyrics = "Hello", Duration = 24 };
        KaraokeClipViewModel clip = new(clipObj, 24, Colors.Goldenrod, "Hello", null, null);
        List<object> targets = [clip];

        PropertyItemViewModel propVm = new(targets, "Lyrics", new Attributes.InspectableAttribute("Lyrics", "Karaoke"), undoMock.Object, new TimelineStructureDocument(), [], null)
        {
            // initial same value
            StringValue = "Hello" // setting to same value
        };

        undoMock.Verify(u => u.Record(It.IsAny<Action>(), It.IsAny<Action>()), Times.Never);

        // different value should record
        propVm.StringValue = "World";
        undoMock.Verify(u => u.Record(It.IsAny<Action>(), It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void MultiTarget_ColorPicker_AllTargetsReceiveColor_NoneBecomesTransparent()
    {
        // Arrange: 3 MoveDefinitionViewModels as redirect targets (simulates IHasSharedColorSource redirect)
        MoveDefinitionViewModel d1 = new() { Id = "a", Color = Colors.Red };
        MoveDefinitionViewModel d2 = new() { Id = "b", Color = Colors.Green };
        MoveDefinitionViewModel d3 = new() { Id = "c", Color = Colors.Blue };

        UndoService undoService = new();
        List<object> targets = [d1, d2, d3];
        PropertyItemViewModel propVm = new(targets, "Color",
            new Attributes.InspectableAttribute("Color", "Appearance"),
            undoService, new TimelineStructureDocument(), [], null);

        // Act: simulate color picker flow
        propVm.OnColorPickerOpened();
        propVm.Value = Colors.Yellow;
        propVm.OnColorPickerClosed();

        // Assert: all targets should be Yellow (none should be Transparent/White)
        Assert.Equal(Colors.Yellow, d1.Color);
        Assert.Equal(Colors.Yellow, d2.Color);
        Assert.Equal(Colors.Yellow, d3.Color);

        // Only one undo entry should exist
        Assert.True(undoService.CanUndo);
        undoService.Undo();

        // After undo, each should be back to its original color
        Assert.Equal(Colors.Red, d1.Color);
        Assert.Equal(Colors.Green, d2.Color);
        Assert.Equal(Colors.Blue, d3.Color);

        // Redo should restore all to Yellow
        undoService.Redo();
        Assert.Equal(Colors.Yellow, d1.Color);
        Assert.Equal(Colors.Yellow, d2.Color);
        Assert.Equal(Colors.Yellow, d3.Color);

        // Only one undo entry: redo again should be a no-op (can't redo further)
        Assert.False(undoService.CanRedo);
    }

    [Fact]
    public void MultiTarget_ValueSetter_NoBatchCorruption()
    {
        // Arrange: targets that fire PropertyChanged (ObservableObject)
        MoveDefinitionViewModel d1 = new() { Id = "a", Color = Colors.Red };
        MoveDefinitionViewModel d2 = new() { Id = "b", Color = Colors.Green };

        UndoService undoService = new();
        List<object> targets = [d1, d2];
        PropertyItemViewModel propVm = new(targets, "Color",
            new Attributes.InspectableAttribute("Color", "Appearance"),
            undoService, new TimelineStructureDocument(), [], null)
        {
            // Act: direct Value set (outside color picker)
            Value = Colors.Orange
        };

        // Assert: both targets should be Orange, not corrupted by intermediate notifications
        Assert.Equal(Colors.Orange, d1.Color);
        Assert.Equal(Colors.Orange, d2.Color);
    }
}