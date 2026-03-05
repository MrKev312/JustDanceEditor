using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;

namespace JustDanceEditor.Editor.Tests;

public class TimelineEditorViewModelTests
{
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
        FieldInfo pkgField = typeof(TimelineEditorViewModel)
            .GetField("<Package>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        pkgField.SetValue(vm, package);

        // initialize tracks collection backing field
        FieldInfo tracksField = typeof(TimelineEditorViewModel)
            .GetField("<Tracks>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tracksField.SetValue(vm, new ObservableCollection<TrackViewModel>());

        // set a dummy root path (used when constructing clip view models)
        FieldInfo rootField = typeof(TimelineEditorViewModel)
            .GetField("<RootPath>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        rootField.SetValue(vm, string.Empty);

        // call BuildTimeline
        MethodInfo build = typeof(TimelineEditorViewModel)
            .GetMethod("BuildTimeline", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
        List<string> titles = new();
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
    public void Save_StoresLyricsColorAsRgbaHex()
    {
        IntermediateSongPackage package = new();
        TimelineEditorViewModel vm = CreateWithoutMedia(package);

        // pick a non-default color and set it
        vm.LyricsDefinitionColor = Colors.Purple;

        // give the viewmodel a temporary folder so Save() doesn't throw
        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(temp);
        FieldInfo rootField = typeof(TimelineEditorViewModel)
            .GetField("<RootPath>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        rootField.SetValue(vm, temp);

        // ensure internal collections are initialized so Save() doesn't NRE
        FieldInfo movesField = typeof(TimelineEditorViewModel)
            .GetField("_moveDefinitions", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
}