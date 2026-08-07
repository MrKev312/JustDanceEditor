using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Assets;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class AssetResolverTests
{
    [Fact]
    public void Should_Resolve_JD2015_MapFolder_Consistently()
    {
        JD2015LayoutResolver resolver = new();
        string cookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2015);
        string uncookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2015);

        Assert.Equal(Path.Combine("world", "jd2015", "song"), cookedMap);
        Assert.Equal(Path.Combine("world", "jd2015", "song"), uncookedMap);
        Assert.Equal(cookedMap, uncookedMap);
    }

    [Fact]
    public void GetAlbumCoach_Returns_AlbumCoach_File_When_Present()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string menuArtFolder = Path.Combine(root, "world", "maps", "song", "menuart", "textures");
        Directory.CreateDirectory(menuArtFolder);

        string fileName = "song_cover_albumcoach.png";
        string path = Path.Combine(menuArtFolder, fileName);
        File.WriteAllText(path, "PNG");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Uncooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        CookedFile file = resolver.GetAlbumCoach() ?? throw new System.InvalidOperationException("Expected album coach asset to be resolved.");

        Assert.EndsWith(fileName, file.RelativePath);

        Directory.Delete(root, true);
    }

    [Fact]
    public void GetAlbumCoach_Returns_JduServer_AlbumCoach_File_When_Present()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string menuArtFolder = Path.Combine(root, "world", "maps", "360", "menuart", "textures");
        Directory.CreateDirectory(menuArtFolder);

        string fileName = "360_AlbumCoach.tga.ckd";
        File.WriteAllText(Path.Combine(menuArtFolder, fileName), "TGA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.NX, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "360") { Type = CookedType.Cooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        CookedFile file = resolver.GetAlbumCoach() ?? throw new System.InvalidOperationException("Expected album coach asset to be resolved.");

        Assert.EndsWith(fileName, file.RelativePath, System.StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, true);
    }

    [Fact]
    public void GetCoachTextures_Returns_JduServer_Coach_Files_Without_Underscore()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string menuArtFolder = Path.Combine(root, "world", "maps", "360", "menuart", "textures");
        Directory.CreateDirectory(menuArtFolder);

        File.WriteAllText(Path.Combine(menuArtFolder, "360_coach1.tga.ckd"), "TGA");
        File.WriteAllText(Path.Combine(menuArtFolder, "360_coach_02.tga.ckd"), "TGA");
        File.WriteAllText(Path.Combine(menuArtFolder, "360_coach_phone.tga.ckd"), "TGA");
        File.WriteAllText(Path.Combine(menuArtFolder, "360_AlbumCoach.tga.ckd"), "TGA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.NX, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "360") { Type = CookedType.Cooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        CookedFile[] files = resolver.GetCoachTextures();

        Assert.Equal(2, files.Length);
        Assert.EndsWith("360_coach1.tga.ckd", files[0].RelativePath, System.StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("360_coach_02.tga.ckd", files[1].RelativePath, System.StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, true);
    }

    [Fact]
    public void GetCoachTextures_Ignores_Uncooked_Atlas_Metadata()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string menuArtFolder = Path.Combine(root, "world", "maps", "song", "menuart", "textures");
        Directory.CreateDirectory(menuArtFolder);

        File.WriteAllText(Path.Combine(menuArtFolder, "song_coach_1.atl"), "ATLAS");
        File.WriteAllText(Path.Combine(menuArtFolder, "song_coach_1.tga"), "TGA");
        File.WriteAllText(Path.Combine(menuArtFolder, "song_coach_1.tga.tfi"), "TFI");
        File.WriteAllText(Path.Combine(menuArtFolder, "song_coach_1_phone.png"), "PNG");
        File.WriteAllText(Path.Combine(menuArtFolder, "song_coach.ignore"), "IGNORE");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2021, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Uncooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        CookedFile file = Assert.Single(resolver.GetCoachTextures());

        Assert.EndsWith("song_coach_1.tga", file.RelativePath, System.StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, true);
    }

    [Fact]
    public void VideoSelector_Prefers_ExactSongVideo_OverAlphaVariant()
    {
        CookedFile selected = UbiArtVideoFileSelector.ChoosePreferredVideoFile(
            [
                new("world/jd5/starships/videoscoach/starships_alpha.webm"),
                new("world/jd5/starships/videoscoach/starships.webm")
            ],
            "Starships") ?? throw new System.InvalidOperationException("Expected video file.");

        Assert.EndsWith("starships.webm", selected.RelativePath, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VideoSelector_ExcludesAlphaVariant_WhenExactSongVideoExists()
    {
        CookedFile[] selected = UbiArtVideoFileSelector.ChoosePreferredVideoFiles(
            [
                new("world/jd5/justdance/videoscoach/justdance.webm"),
                new("world/jd5/justdance/videoscoach/justdance_alpha.webm")
            ],
            "JustDance");

        CookedFile selectedFile = Assert.Single(selected);
        Assert.EndsWith("justdance.webm", selectedFile.RelativePath, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VideoSelector_UsesAlphaVariant_WhenItIsTheOnlyVideo()
    {
        CookedFile selected = UbiArtVideoFileSelector.ChoosePreferredVideoFile(
            [
                new("world/jd5/song/videoscoach/song_alpha.webm")
            ],
            "Song") ?? throw new System.InvalidOperationException("Expected video file.");

        Assert.EndsWith("song_alpha.webm", selected.RelativePath, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VideoSelector_Returns_All_MediaQualityVariants_When_AtMostFour()
    {
        CookedFile[] selected = UbiArtVideoFileSelector.ChoosePreferredVideoFiles(
            [
                new("world/maps/360/media/360_LOW.hd.webm"),
                new("world/maps/360/media/360_ULTRA.hd.webm"),
                new("world/maps/360/media/360_MID.hd.webm"),
                new("world/maps/360/media/360_HIGH.hd.webm")
            ],
            "360");

        Assert.Equal(4, selected.Length);
        Assert.EndsWith("360_ULTRA.hd.webm", selected[0].RelativePath, System.StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("360_HIGH.hd.webm", selected[1].RelativePath, System.StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("360_MID.hd.webm", selected[2].RelativePath, System.StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("360_LOW.hd.webm", selected[3].RelativePath, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VideoSelector_Prefers_InputMediaFolder_When_CookedCacheHasExtraNamedVideo()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mediaFolder = Path.Combine(root, "world", "maps", "360", "media");
        string cacheMediaFolder = Path.Combine(root, "cache", "itf_cooked", "nx", "world", "maps", "360", "media");
        Directory.CreateDirectory(mediaFolder);
        Directory.CreateDirectory(cacheMediaFolder);

        string[] hashFiles =
        [
            "0f1bd7bd01be7fef2467e340a5fb6ff9.webm",
            "c764961cb68befd9e503f82f43628baf.webm",
            "f56ca4fa3506f651da3a84e7f9e50829.webm",
            "f6189a278891b7cb6f118ee9fcbdefd0.webm"
        ];

        foreach (string file in hashFiles)
            File.WriteAllText(Path.Combine(mediaFolder, file), "WEBM");

        File.WriteAllText(Path.Combine(cacheMediaFolder, "360_ULTRA.hd.webm"), "OLD");

        UbiArtVersionProfile profile = new(UbiArtPlatform.NX, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "360") { Type = CookedType.Cooked };
        using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        CookedFile[] selected = UbiArtVideoFileSelector.FindPreferredVideoFiles(fs);

        Assert.Equal(4, selected.Length);
        Assert.DoesNotContain(selected, file => file.RelativePath.Contains("360_ULTRA", System.StringComparison.OrdinalIgnoreCase));
        Assert.All(hashFiles, file => Assert.Contains(selected, selectedFile => selectedFile.RelativePath.EndsWith(file, System.StringComparison.OrdinalIgnoreCase)));

        Directory.Delete(root, true);
    }

    [Fact]
    public void VideoSelector_Prefers_QualitySet_When_MoreThanFourVideosExist()
    {
        CookedFile[] selected = UbiArtVideoFileSelector.ChoosePreferredVideoFiles(
            [
                new("world/maps/song/media/song_preview.webm"),
                new("world/maps/song/media/song_LOW.hd.webm"),
                new("world/maps/song/media/song_ALPHA.webm"),
                new("world/maps/song/media/song_ULTRA.hd.webm"),
                new("world/maps/song/media/song_MID.hd.webm"),
                new("world/maps/song/media/song_HIGH.hd.webm")
            ],
            "song");

        Assert.Equal(4, selected.Length);
        Assert.All(selected, file => Assert.DoesNotContain("preview", file.RelativePath, System.StringComparison.OrdinalIgnoreCase));
        Assert.All(selected, file => Assert.DoesNotContain("ALPHA", file.RelativePath, System.StringComparison.OrdinalIgnoreCase));
    }
}
