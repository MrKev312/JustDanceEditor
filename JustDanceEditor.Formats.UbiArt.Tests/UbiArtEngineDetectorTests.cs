using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtEngineDetectorTests
{
    [Fact]
    public void Detect_ModernCooked_Should_Peek_JDVersion_From_SongDesc()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mapsFolder = Path.Combine(root, "world", "maps", "song");
        Directory.CreateDirectory(mapsFolder);

        // Create a JSON songdesc with JDVersion
        File.WriteAllText(Path.Combine(mapsFolder, "songdesc.tpl"), "{ \"COMPONENTS\": [ { \"JDVersion\": 4884 } ] }");

        UbiArtEngineDetector detector = new();
        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtContainerStyle.Uncooked, profile.ContainerStyle);
        Assert.Equal(UbiArtEngineVersion.Modern, profile.EngineVersion);
        Assert.Equal((uint)4884, profile.EngineNumericVersion);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Detect_JD2014_Uncooked_When_world_maps_jd5_exists()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "world", "maps", "jd5"));

        UbiArtEngineDetector detector = new();
        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtContainerStyle.Uncooked, profile.ContainerStyle);
        Assert.Equal(UbiArtEngineVersion.JD2014, profile.EngineVersion);
        Assert.IsType<UbiArtLayoutResolver>(profile.Layout);
        Assert.IsType<LuaUbiArtSerializer>(profile.Serializer);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Detect_Uncooked_When_flat_and_tpl_exists()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "songdesc.tpl"), "params = {}\n");

        UbiArtEngineDetector detector = new();
        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtContainerStyle.Uncooked, profile.ContainerStyle);
        Assert.Equal(UbiArtEngineVersion.Modern, profile.EngineVersion);
        Assert.IsType<UbiArtLayoutResolver>(profile.Layout);
        Assert.IsType<LuaUbiArtSerializer>(profile.Serializer);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Layout_Should_Resolve_JD2014_Uncooked_MapFolder()
    {
        UbiArtLayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "maps", "jd5", "song"), mapFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_JD2014_Cooked_MapFolder()
    {
        UbiArtLayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "jd5", "song"), mapFolder);
    }
}