using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using Avalonia.Media;

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

        // set the private readonly _package field
        FieldInfo pkgField = typeof(TimelineEditorViewModel)
            .GetField("_package", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
}
