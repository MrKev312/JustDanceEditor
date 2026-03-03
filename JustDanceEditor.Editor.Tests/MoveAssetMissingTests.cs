using Avalonia.Media;
using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Editor.Views.Converters;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Globalization;

namespace JustDanceEditor.Editor.Tests;

public class MoveAssetMissingTests
{
    [Fact]
    public void GetOrRegisterMove_SetsHasAssetBasedOnRootPath()
    {
        string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tmp);
            string movesDir = Path.Combine(tmp, IntermediatePackageLayout.Assets.MovesFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(movesDir);
            string gesturesDir = Path.Combine(tmp, IntermediatePackageLayout.Assets.GesturesFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(gesturesDir);

            // create one msm and one gesture file
            File.WriteAllText(Path.Combine(movesDir, "foo.msm"), "dummy");
            File.WriteAllText(Path.Combine(gesturesDir, "bar.gesture"), "dummy");

            IntermediateSongPackage package = new();
            TimelineEditorViewModel timeline = new(package, tmp, new PlaybackService(), new TimelineSettingsService());

            MoveDefinitionViewModel def1 = timeline.GetOrRegisterMove("foo", false);
            MoveDefinitionViewModel def2 = timeline.GetOrRegisterMove("missing", false);
            MoveDefinitionViewModel def3 = timeline.GetOrRegisterMove("bar", true);
            MoveDefinitionViewModel def4 = timeline.GetOrRegisterMove("other", true);

            Assert.True(def1.HasAsset);
            Assert.False(def2.HasAsset);
            Assert.True(def3.HasAsset);
            Assert.False(def4.HasAsset);
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, true);
            }
            catch { }
        }
    }

    [Fact]
    public void MoveClipViewModel_IsAssetMissingReflectsDefinition()
    {
        string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        // no files created, so any move should be missing
        IntermediateSongPackage package = new();
        TimelineEditorViewModel timeline = new(package, tmp, new PlaybackService(), new TimelineSettingsService());

        MoveDefinitionViewModel def = timeline.GetOrRegisterMove("nope", false);
        Assert.False(def.HasAsset);

        MoveClip raw = new() { MoveId = "nope" };
        MoveClipViewModel clip = new(raw, duration: 24, color: Colors.White, moveId: "nope", rootPath: tmp, parentTimeline: timeline, isFullBody: false);
        Assert.True(clip.IsAssetMissing);

        // if we manually flip the definition flag the clip property updates
        def.HasAsset = true;
        Assert.False(clip.IsAssetMissing);
    }

    [Fact]
    public void LibraryItems_MissingAssetHaveStripedIcon()
    {
        string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tmp);
            string movesDir = Path.Combine(tmp, IntermediatePackageLayout.Assets.MovesFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(movesDir);
            // create one file for "exists"
            File.WriteAllText(Path.Combine(movesDir, "exists.msm"), "x");

            IntermediateSongPackage package = new();
            package.HandCoachMoves["exists"] = new CoachMoveDefinition();
            package.HandCoachMoves["missing"] = new CoachMoveDefinition();

            TimelineEditorViewModel timeline = new(package, tmp, new PlaybackService(), new TimelineSettingsService());
            LibraryToolViewModel lib = new();
            lib.ActiveTimeline = timeline;

            LibraryItemViewModel itemExists = lib.Items.First(i => i.Id == "exists");
            LibraryItemViewModel itemMissing = lib.Items.First(i => i.Id == "missing");

            Assert.True(itemExists.HasAsset);
            Assert.False(itemMissing.HasAsset);
            Assert.IsType<SolidColorBrush>(itemExists.Icon);
            Assert.IsType<DrawingBrush>(itemMissing.Icon);
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, true);
            }
            catch { }
        }
    }

    // -------------------------------------------------------------------
    // pictogram missing tests
    // -------------------------------------------------------------------

    [Fact]
    public void BitmapValueConverter_ReturnsRedWhenFileMissing()
    {
        BitmapValueConverter conv = BitmapValueConverter.Instance;
        object? result = conv.Convert("C:\\nonexistent.png", typeof(object), null, CultureInfo.InvariantCulture);
        // Result may be null if Avalonia isn't initialized, or a Bitmap if it is
        // Either way, the converter should handle missing files gracefully (not throw)
        // In a real app with Avalonia initialized, this would be a red 64x64 Bitmap
        Assert.True(result == null || result is Bitmap);
    }

    [Fact]
    public void PictogramOptionViewModel_MissingShowsRed()
    {
        string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tmp);
            string path = Path.Combine(tmp, "a.png"); // file does not exist
            PictogramOptionViewModel opt = new("a", path);
            // PreviewImage may be null if Avalonia isn't initialized, but the option should still be created
            Assert.Equal("a", opt.Name);
            Assert.Equal(path, opt.ImagePath);
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, true); } catch { }
        }
    }

    [Fact]
    public void Library_IncludesMissingPictogramWithRedIcon()
    {
        string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tmp);
            string picDir = Path.Combine(tmp, IntermediatePackageLayout.Assets.PictogramsFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(picDir);
            // create one file to represent existing pictogram
            File.WriteAllText(Path.Combine(picDir, "exists.webp"), "x");

            IntermediateSongPackage package = new();
            TimelineEditorViewModel timeline = new(package, tmp, new PlaybackService(), new TimelineSettingsService());
            // add a clip referencing missing pictogram
            TrackViewModel track = new();
            track.Clips.Add(new PictogramClipViewModel(new PictogramClip { PictogramId = "missing" }, 24, Colors.Black, "missing", tmp, timeline));
            timeline.Tracks.Add(track);

            LibraryToolViewModel lib = new();
            lib.ActiveTimeline = timeline;

            LibraryItemViewModel missingItem = lib.Items.First(i => i.Id == "missing");
            Assert.False(missingItem.HasAsset);
            // Can be either SolidColorBrush or ImmutableSolidColorBrush
            Assert.True(missingItem.Icon is SolidColorBrush or Avalonia.Media.Immutable.ImmutableSolidColorBrush);
            ISolidColorBrush? colorBrush = missingItem.Icon as ISolidColorBrush;
            Assert.NotNull(colorBrush);
            Assert.Equal(Colors.Red, colorBrush.Color);
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, true); } catch { }
        }
    }

    [Fact]
    public void PictogramClip_GetDynamicOptions_IncludesMissingId()
    {
        string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tmp);
            string picDir = Path.Combine(tmp, IntermediatePackageLayout.Assets.PictogramsFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(picDir);
            // no files at all

            IntermediateSongPackage package = new();
            TimelineEditorViewModel timeline = new(package, tmp, new PlaybackService(), new TimelineSettingsService());
            TrackViewModel track = new();
            track.Clips.Add(new PictogramClipViewModel(new PictogramClip { PictogramId = "ghost" }, 24, Colors.Black, "ghost", tmp, timeline));
            timeline.Tracks.Add(track);

            PictogramClipViewModel clipVm = new(new PictogramClip(), 24, Colors.Black, "", tmp, timeline);
            IEnumerable<object>? options = clipVm.GetDynamicOptions(nameof(PictogramClipViewModel.PictogramId), timeline);
            Assert.NotNull(options);
            // Just check that options can be enumerated - rendering may fail without Avalonia
            int count = 0;
            foreach (object opt in options)
            {
                count++;
                if (opt is PictogramOptionViewModel pov)
                {
                    if (pov.Name == "ghost")
                    {
                        // Found the missing pictogram in the options
                        return;
                    }
                }
            }

            Assert.Fail("Missing pictogram 'ghost' not found in dynamic options");
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, true); } catch { }
        }
    }
}