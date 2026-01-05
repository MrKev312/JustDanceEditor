using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using System.IO;

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
}