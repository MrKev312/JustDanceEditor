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

public class AssetResolverExtensionTests
{
    [Fact]
    public void Should_Find_Texture_With_Extension_Fallback()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string pictosFolder = Path.Combine(root, "world", "maps", "song", "timeline", "pictos");
        Directory.CreateDirectory(pictosFolder);

        // Only cooked TGA exists
        File.WriteAllText(Path.Combine(pictosFolder, "picto.tga.ckd"), "TGADATA");
        Directory.CreateDirectory(Path.Combine(root, "cache", "itf_cooked", "nx"));

        var profile = new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song");
        FileSystem fs = new(req, NullLogger<FileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        var resolver = new FileSystemAssetResolver(fs.Layout!, fs);
        CookedFile picto = resolver.FindPictogram("picto");
        Assert.NotNull(picto);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Uncooked_Asset_Resolution_Finds_PNG()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string pictosFolder = Path.Combine(root, "timeline", "pictos");
        Directory.CreateDirectory(pictosFolder);

        File.WriteAllText(Path.Combine(pictosFolder, "picto.png"), "PNGDATA");

        var profile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), null)
        {
            Type = UbiArtType.Uncooked
        };
        FileSystem fs = new(req, NullLogger<FileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        var resolver = new FileSystemAssetResolver(fs.Layout!, fs);
        CookedFile picto = resolver.FindPictogram("picto");
        Assert.NotNull(picto);

        Directory.Delete(root, true);
    }
}