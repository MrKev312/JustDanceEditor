using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JustDanceEditor.Editor.Tests;

public class TimelineEditorViewModelTests
{
    private static FieldInfo GetRequiredField(string name)
    {
        return typeof(TimelineEditorViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(TimelineEditorViewModel).FullName, name);
    }

    private static MethodInfo GetRequiredMethod(string name)
    {
        return typeof(TimelineEditorViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(TimelineEditorViewModel).FullName, name);
    }

    /// <summary>
    /// Helper to create a timeline viewmodel without invoking media initialization.
    /// Uses <see cref="RuntimeHelpers.GetUninitializedObject"/> to avoid the constructor.
    /// The private backing fields for Tracks, RootPath, and _package are then populated and
    /// <see cref="TimelineEditorViewModel.BuildTimeline"/> is invoked via reflection.
    /// </summary>
    private static TimelineEditorViewModel CreateWithoutMedia(IntermediateSongPackage package)
    {
        TimelineEditorViewModel vm = (TimelineEditorViewModel)RuntimeHelpers.GetUninitializedObject(typeof(TimelineEditorViewModel));

        // set the Package backing field
        FieldInfo pkgField = GetRequiredField("<Package>k__BackingField");
        pkgField.SetValue(vm, package);

        // initialize tracks collection backing field
        FieldInfo tracksField = GetRequiredField("<Tracks>k__BackingField");
        tracksField.SetValue(vm, new ObservableCollection<TrackViewModel>());

        // set a dummy root path (used when constructing clip view models)
        FieldInfo rootField = GetRequiredField("<RootPath>k__BackingField");
        rootField.SetValue(vm, string.Empty);

        FieldInfo movesField = GetRequiredField("_moveDefinitions");
        movesField.SetValue(vm, new Dictionary<(string, bool), MoveDefinitionViewModel>());

        // call BuildTimeline
        MethodInfo build = GetRequiredMethod("BuildTimeline");
        build.Invoke(vm, null);

        return vm;
    }

    [Fact]
    public void BuildTimeline_IncludesHideHudTrackAboveLyrics()
    {
        // arrange: package with a single hide hud clip
        IntermediateSongPackage package = new();
        package.HideUserInterface.Clips.Add(new HideUserInterfaceClip { StartTime = 100, Duration = 48, IsActive = true });

        // act
        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        // assert: track exists and is before lyrics
        List<string> titles = [];
        foreach (TrackViewModel t in vm.Tracks)
            titles.Add(t.Title);

        int hideIndex = titles.IndexOf("Hide HUD");
        int lyricsIndex = titles.IndexOf("Lyrics");

        Assert.InRange(hideIndex, 0, titles.Count - 1);
        Assert.InRange(lyricsIndex, 0, titles.Count - 1);
        Assert.True(hideIndex < lyricsIndex, "Hide HUD track should appear before Lyrics track");

        // there should be exactly one clip of the correct view model type
        TrackViewModel hideTrack = vm.Tracks[hideIndex];
        Assert.Single(hideTrack.Clips);
        Assert.IsType<HideUserInterfaceClipViewModel>(hideTrack.Clips[0]);
        // track color should be purple
        Assert.Equal(Colors.MediumPurple, hideTrack.TrackColor);
        Assert.Equal(Colors.MediumPurple, ((HideUserInterfaceClipViewModel)hideTrack.Clips[0]).BackgroundColor);
    }

    [Fact]
    public void BuildTimeline_IncludesEmptyVideoTrackBeforeHideHud()
    {
        IntermediateSongPackage package = new();

        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        List<string> titles = [.. vm.Tracks.Select(t => t.Title)];
        int videoIndex = titles.IndexOf("Video");
        int hideHudIndex = titles.IndexOf("Hide HUD");

        Assert.InRange(videoIndex, 0, titles.Count - 1);
        Assert.InRange(hideHudIndex, 0, titles.Count - 1);
        Assert.True(videoIndex < hideHudIndex, "Video track should appear before Hide HUD");

        TrackViewModel videoTrack = vm.Tracks[videoIndex];
        Assert.Equal(TrackType.Video, videoTrack.TrackType);
        Assert.Empty(videoTrack.Clips);
    }

    [Fact]
    public void Save_StoresLyricsColorAsRgbaHex()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        // pick a non-default color and set it
        vm.LyricsDefinitionColor = Colors.Purple;

        // give the viewmodel a temporary folder so Save() doesn't throw
        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(temp);
        FieldInfo rootField = GetRequiredField("<RootPath>k__BackingField");
        rootField.SetValue(vm, temp);

        // ensure internal collections are initialized so Save() doesn't NRE
        FieldInfo movesField = GetRequiredField("_moveDefinitions");
        movesField.SetValue(vm, new Dictionary<(string, bool), MoveDefinitionViewModel>());

        try
        {
            vm.Save();
        }
        catch
        {
            // some internal fields (e.g. UndoService) are not initialized when using
            // the uninitialized-object trick; it's fine as long as metadata was updated.
        }

        Assert.Equal(ClipViewModel.ColorToRgbaHex(Colors.Purple), package.Metadata.LyricsColor);

        Directory.Delete(temp, true);
    }

    [Fact]
    public void BuildTimeline_ParsesLegacyRgbaMoveColorWithoutChannelSwap()
    {
        IntermediateSongPackage package = new();
        package.HandCoachMoves["moveA"] = new CoachMoveDefinition
        {
            Color = ClipViewModel.ColorToRgbaHex(Colors.Red),
            Duration = 24
        };

        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        MoveDefinitionViewModel def = vm.GetOrRegisterMove("moveA", false);
        Assert.Equal(Colors.Red, def.Color);
    }

    [Fact]
    public void BuildTimeline_ParsesRgbMoveColorWithoutDefaultingToWhite()
    {
        IntermediateSongPackage package = new();
        package.HandCoachMoves["moveA"] = new CoachMoveDefinition
        {
            Color = "#CCCCCC",
            Duration = 24
        };

        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        MoveDefinitionViewModel def = vm.GetOrRegisterMove("moveA", false);
        Assert.Equal(new Color(255, 0xCC, 0xCC, 0xCC), def.Color);
    }

    [Fact]
    public void Save_StoresMoveColorsAsRgbHex()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        MoveDefinitionViewModel def = new()
        {
            Id = "moveA",
            IsFullBody = false,
            Color = Colors.Red,
            DefaultDuration = 24
        };

        FieldInfo movesField = GetRequiredField("_moveDefinitions");
        movesField.SetValue(vm, new Dictionary<(string, bool), MoveDefinitionViewModel>
        {
            [("moveA", false)] = def
        });

        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(temp);
        FieldInfo rootField = GetRequiredField("<RootPath>k__BackingField");
        rootField.SetValue(vm, temp);

        try
        {
            vm.Save();
        }
        catch
        {
            // Save may still touch uninitialized fields when using the uninitialized-object helper.
        }

        Assert.Equal("#FF0000", package.HandCoachMoves["moveA"].Color);

        Directory.Delete(temp, true);
    }
}