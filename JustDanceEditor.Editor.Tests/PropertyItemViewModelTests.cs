using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using Moq;

namespace JustDanceEditor.Editor.Tests;

public class PropertyItemViewModelTests
{
    [Fact]
    public void Registry_RedirectsMoveColorToTypedDefinitionTarget()
    {
        IntermediateSongPackage package = new();
        package.HandCoachMoves["wave"] = new CoachMoveDefinition
        {
            Duration = 24,
            Color = "#FF0000FF"
        };
        using TimelineEditorViewModel timeline = new(
            package,
            string.Empty,
            new PlaybackService(),
            new TimelineSettingsService());
        MoveClipViewModel clip = new(new MoveClip { MoveId = "wave" }, parentTimeline: timeline);

        ResolvedInspectableProperty color = new InspectablePropertyRegistry()
            .Resolve([clip], timeline)
            .Single(property => property.Descriptor.Key == nameof(MoveClipViewModel.BackgroundColor));

        Assert.Same(clip.Definition, Assert.Single(color.Targets));
        Assert.Equal(nameof(MoveDefinitionViewModel.Color), color.Descriptor.NotificationPropertyName);
    }

    [Fact]
    public void Registry_MixedClipTypesExposeOnlyCommonTimingProperties()
    {
        using TimelineEditorViewModel timeline = new(
            new IntermediateSongPackage(),
            string.Empty,
            new PlaybackService(),
            new TimelineSettingsService());
        MoveClipViewModel move = new(new MoveClip { MoveId = "wave" }, parentTimeline: timeline);
        KaraokeClipViewModel lyric = new(new KaraokeClip { Lyrics = "Hello", Duration = 24 }, parentTimeline: timeline);

        IReadOnlyList<ResolvedInspectableProperty> properties =
            new InspectablePropertyRegistry().Resolve([move, lyric], timeline);

        Assert.Equal(
            [nameof(ClipViewModel.StartBeat), nameof(ClipViewModel.DurationBeats)],
            properties.Select(static property => property.Descriptor.Key));
    }

    [Fact]
    public void SettingSameValue_DoesNotRecordUndo()
    {
        Mock<IUndoService> undoMock = new();
        KaraokeClip clipObj = new() { Lyrics = "Hello", Duration = 24 };
        KaraokeClipViewModel clip = new(clipObj, null, null);
        List<object> targets = [clip];

        InspectablePropertyDescriptor<KaraokeClipViewModel, string> descriptor = new(
            nameof(KaraokeClipViewModel.Lyrics), "Lyrics", "Karaoke",
            static clip => clip.Lyrics,
            static (clip, value) => clip.Lyrics = value);
        PropertyItemViewModel propVm = new(targets, descriptor, undoMock.Object)
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
        // Arrange: 3 canonical move-definition color targets.
        MoveDefinitionViewModel d1 = new() { Id = "a", Color = Colors.Red };
        MoveDefinitionViewModel d2 = new() { Id = "b", Color = Colors.Green };
        MoveDefinitionViewModel d3 = new() { Id = "c", Color = Colors.Blue };

        UndoService undoService = new();
        List<object> targets = [d1, d2, d3];
        InspectablePropertyDescriptor<MoveDefinitionViewModel, Color> descriptor = new(
            nameof(MoveDefinitionViewModel.Color), "Color", "Appearance",
            static definition => definition.Color,
            static (definition, value) => definition.Color = value);
        PropertyItemViewModel propVm = new(targets, descriptor, undoService);

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
        InspectablePropertyDescriptor<MoveDefinitionViewModel, Color> descriptor = new(
            nameof(MoveDefinitionViewModel.Color), "Color", "Appearance",
            static definition => definition.Color,
            static (definition, value) => definition.Color = value);
        PropertyItemViewModel propVm = new(targets, descriptor, undoService)
        {
            // Act: direct Value set (outside color picker)
            Value = Colors.Orange
        };

        // Assert: both targets should be Orange, not corrupted by intermediate notifications
        Assert.Equal(Colors.Orange, d1.Color);
        Assert.Equal(Colors.Orange, d2.Color);
    }

    [Fact]
    public void NumericInspectable_UsesSliderBinding()
    {
        NumericTarget target = new() { Threshold = 0.5 };
        InspectablePropertyDescriptor<NumericTarget, double> descriptor = new(
            nameof(NumericTarget.Threshold), "Threshold", "MSM",
            static item => item.Threshold,
            static (item, value) => item.Threshold = value,
            new NumericPropertyRange(-1.0, 1.4, 0.01));
        PropertyItemViewModel propVm = new([target], descriptor, new UndoService());

        Assert.True(propVm.ShowSlider);
        Assert.False(propVm.ShowTextBox);
        Assert.Equal(-1.0, propVm.Minimum);
        Assert.Equal(1.4, propVm.Maximum);
        Assert.Equal(0.01, propVm.TickFrequency);

        propVm.NumericValue = 1.2;

        Assert.Equal(1.2, target.Threshold);
    }

    [Fact]
    public void GetterOnlyNumericProperty_IgnoresHiddenSliderWrite()
    {
        ReadOnlyNumericTarget target = new();
        InspectablePropertyDescriptor<ReadOnlyNumericTarget, int> descriptor = new(
            nameof(ReadOnlyNumericTarget.Score), "Score", "MSM",
            static item => item.Score);
        PropertyItemViewModel propVm = new([target], descriptor, new UndoService());

        Assert.True(propVm.IsReadOnly);
        Assert.False(propVm.ShowSlider);
        Assert.True(propVm.ShowReadOnlyText);

        propVm.NumericValue = 0;
        propVm.Value = 0;

        Assert.Equal(42, target.Score);
    }

    private sealed class NumericTarget
    {
        public double Threshold { get; set; }
    }

    private sealed class ReadOnlyNumericTarget
    {
        public int Score => 42;
    }
}
