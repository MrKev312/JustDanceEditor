using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Assets;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class AssetResolverTests
{
    [Fact]
    public void Should_Find_Pictogram_Regardless_Of_Style()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string pictosFolder = Path.Combine(root, "world", "jd2015", "song", "timeline", "pictos");
        Directory.CreateDirectory(pictosFolder);

        // Cooked: picto.png.ckd
        File.WriteAllText(Path.Combine(pictosFolder, "picto.png.ckd"), "PNGDATA");
        // Create cooked platform folder so InitializePlatformType succeeds
        Directory.CreateDirectory(Path.Combine(root, "cache", "itf_cooked", "nx"));

        var profile = new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song");
        LayeredFileSystem fs = new(req, NullLogger<LayeredFileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        var resolver = new FileSystemAssetResolver(fs.Layout!, fs);
        CookedFile? picto = resolver.FindPictogram("picto");
        Assert.NotNull(picto);

        // Cleanup and test uncooked
        Directory.Delete(root, true);

        root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        pictosFolder = Path.Combine(root, "world", "maps", "song", "timeline", "pictos");
        Directory.CreateDirectory(pictosFolder);
        File.WriteAllText(Path.Combine(pictosFolder, "picto.png"), "PNGDATA");

        profile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        req = new(root, Path.GetTempPath(), "song")
        {
            Type = UbiArtType.Uncooked // mark as uncooked so platform detection does not require itf_cooked
        };
        fs = new(req, NullLogger<LayeredFileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        resolver = new FileSystemAssetResolver(fs.Layout!, fs);
        picto = resolver.FindPictogram("picto");
        Assert.NotNull(picto);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Should_Resolve_PictosFolder_For_JD2015_Cooked_vs_Uncooked()
    {
        var resolver = new UbiArtLayoutResolver();
        string cookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2015);
        string uncookedMap = resolver.GetMapWorldFolder("/in", "song", UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2015);

        Assert.Equal(Path.Combine("world", "jd2015", "song"), cookedMap);
        Assert.Equal(Path.Combine("world", "maps", "jd2015", "song"), uncookedMap);
    }
}