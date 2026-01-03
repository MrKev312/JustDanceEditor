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
}