using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JustDanceEditor.Editor.Tests;

public class TimelineEditorViewModelTests
{
    private static FieldInfo GetRequiredField(Type type, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name);
    }

    private static FieldInfo GetRequiredField(string name)
    {
        return GetRequiredField(typeof(TimelineEditorViewModel), name);
    }

    private static MethodInfo GetRequiredMethod(string name)
    {
        return typeof(TimelineEditorViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(TimelineEditorViewModel).FullName, name);
    }

    /// <summary>
    /// Helper to create a timeline viewmodel without invoking media initialization.
    /// Uses <see cref="RuntimeHelpers.GetUninitializedObject"/> to avoid the constructor.
    /// The private backing fields and controller fields required by BuildTimeline are populated and
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

        Dictionary<(string, bool), MoveDefinitionViewModel> moveDefinitions = [];
        FieldInfo movesField = GetRequiredField("_moveDefinitions");
        movesField.SetValue(vm, moveDefinitions);

        FieldInfo mediaField = GetRequiredField("_media");
        mediaField.SetValue(vm, new TimelineMediaController(vm));

        FieldInfo lyricsField = GetRequiredField("_lyrics");
        lyricsField.SetValue(vm, new TimelineLyricsController(vm));

        FieldInfo moveDefinitionControllerField = GetRequiredField("_moveDefinitionController");
        moveDefinitionControllerField.SetValue(vm, new TimelineMoveDefinitionController(vm, moveDefinitions));

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

    [Fact]
    public async Task TrySaveAndCloseAfterPromptAsync_WhenSaveFails_KeepsDocumentDirtyAndCloseBlocked()
    {
        string rootFile = Path.GetTempFileName();
        try
        {
            TimelineEditorViewModel vm = CreateSaveFailureTimeline(rootFile);
            vm.UndoService.Record(static () => { }, static () => { });

            bool closed = await vm.TrySaveAndCloseAfterPromptAsync();

            Assert.False(closed);
            Assert.True(vm.UndoService.IsDirty);
            Assert.Equal("Broken Save *", vm.Title);
            object closeController = GetRequiredField("_closeController").GetValue(vm)
                ?? throw new InvalidOperationException("Timeline close controller was not initialized.");
            Assert.False((bool)(GetRequiredField(typeof(TimelineCloseController), "_allowClose").GetValue(closeController) ?? true));
        }
        finally
        {
            File.Delete(rootFile);
        }
    }

    [Fact]
    public async Task SaveMapCommand_RunAsync_WhenSaveFails_ReportsFailureAndKeepsDocumentDirty()
    {
        string rootFile = Path.GetTempFileName();
        try
        {
            TimelineEditorViewModel vm = CreateSaveFailureTimeline(rootFile);
            vm.UndoService.Record(static () => { }, static () => { });

            bool saved = await SaveMapCommand.RunAsync(new TestTimelineContextService(vm));

            Assert.False(saved);
            Assert.True(vm.UndoService.IsDirty);
            Assert.Equal("Broken Save *", vm.Title);
        }
        finally
        {
            File.Delete(rootFile);
        }
    }

    private static TimelineEditorViewModel CreateSaveFailureTimeline(string rootPath)
    {
        IntermediateSongPackage package = new()
        {
            Metadata =
            {
                MapName = "Broken Save"
            }
        };
        package.TimelineStructure.StartBeat = 0;
        package.TimelineStructure.EndBeat = 64;

        return new TimelineEditorViewModel(package, rootPath, new PlaybackService(), new TimelineSettingsService());
    }
}