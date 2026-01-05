using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using System.IO;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class AssetResolverTests
{
    [Fact]
    public void Should_Resolve_PictosFolder_For_JD2015_Cooked_vs_Uncooked()
    {
        UbiArtLayoutResolver resolver = new();
        string cookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2015);
        string uncookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2015);

        Assert.Equal(Path.Combine("world", "jd2015", "song"), cookedMap);
        Assert.Equal(Path.Combine("world", "maps", "jd2015", "song"), uncookedMap);
    }

    [Fact]
    public void GetAlbumCoach_Returns_AlbumCoach_File_When_Present()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string menuArtFolder = Path.Combine(root, "song", "menuart", "textures");
        Directory.CreateDirectory(menuArtFolder);

        string fileName = "song_cover_albumcoach.png";
        string path = Path.Combine(menuArtFolder, fileName);
        File.WriteAllText(path, "PNG");

        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = UbiArtType.Uncooked };
        LayeredFileSystem fs = new(req, NullLogger<LayeredFileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.Layout!, fs);
        CookedFile? file = resolver.GetAlbumCoach();

        Assert.NotNull(file);
        Assert.EndsWith(fileName, file!.FullPath);

        Directory.Delete(root, true);
    }
}