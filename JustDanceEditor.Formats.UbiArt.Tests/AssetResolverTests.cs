using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Assets;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class AssetResolverTests
{
    [Fact]
    public void Should_Resolve_PictosFolder_For_JD2015_Cooked_vs_Uncooked()
    {
        UbiArtLayoutResolver resolver = new();
        string cookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtPlatform.WiiU, UbiArtEngineVersion.JD2015);
        string uncookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2015);

        Assert.Equal(Path.Combine("world", "jd2015", "song"), cookedMap);
        Assert.Equal(Path.Combine("world", "maps", "jd2015", "song"), uncookedMap);
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
        LayeredFileSystem fs = new(req, profile, NullLogger<LayeredFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout!, fs);
        CookedFile? file = resolver.GetAlbumCoach();

        Assert.NotNull(file);
        Assert.EndsWith(fileName, file!.RelativePath);

        Directory.Delete(root, true);
    }
}